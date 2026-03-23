# BMMDL Runtime Events - Design Document

## 1. Current State Analysis

### 1.1 What Exists

| Layer | Component | Status |
|-------|-----------|--------|
| **DSL Grammar** | `event` definition, `emit` statement, `emits` clause, `on change of` trigger | Parsed, AST built |
| **MetaModel** | `BmEvent`, `BmEventField`, `BmEventHandler`, `BmEmitStatement`, `BmAction.Emits` | Models defined |
| **Runtime** | `EventPublisher` (in-memory bus), `ServiceEventHandler`, `AuditLogEventHandler` | Basic wiring |
| **Infrastructure** | `IRealtimeNotifier` (interface only), `IAuditLogStore` (PostgreSQL) | Partial |

### 1.2 Current Flow

```
CRUD Operation
  → RuleEngine.ExecuteBeforeAsync()     [blocking - validation/compute]
  → Database INSERT/UPDATE/DELETE
  → RuleEngine.ExecuteAfterAsync()      [fire-and-forget]
  → EventPublisher.PublishAsync()       [fire-and-forget, in-memory]
      → AuditLogEventHandler            [persists to platform.audit_logs]
      → ServiceEventHandler             [routes to service on EventName { ... }]
      → IRealtimeNotifier               [not implemented]
```

### 1.3 Gaps

- **No durability**: Events are in-memory only; lost on process crash
- **No ordering guarantee**: ConcurrentBag iteration order is undefined
- **No retry/dead letter**: Failed handlers silently logged, never retried
- **No event versioning**: Schema changes break consumers
- **No saga/choreography**: No cross-entity transaction coordination
- **No backpressure**: Unbounded handler execution
- **Service event handler grammar incomplete**: `on EventName { ... }` in services not fully parseable
- ~~**Action `emits` runtime not wired**~~: **DONE** — Grammar parsed + automatic emission via UoW.EnqueueEvent() in EntityActionController + ODataServiceController

### 1.4 Transaction Context Problem

Hiện tại runtime có **lỗ hổng transaction nghiêm trọng** ảnh hưởng trực tiếp đến tính đúng đắn của event system:

| Operation | Vấn đề |
|-----------|--------|
| **CRUD** | Mỗi statement auto-commit riêng, không có transaction wrapper |
| **Deep Insert** | 3 INSERT tuần tự (N:1 → root → 1:N), nếu bước 2 fail → bước 1 đã commit, orphan data |
| **Deep Update** | Multiple UPDATE/INSERT/DELETE riêng biệt, partial update khi fail giữa chừng |
| **Temporal Update** | CLOSE (UPDATE) + INSERT new version: nếu INSERT fail → record bị close nhưng không có version mới |
| **Batch** | "Atomicity" giả lập ở application level, trả 424 nhưng data đã commit |
| **ETag Check** | SELECT rồi UPDATE riêng → race condition giữa check và write |
| **Event Publish** | Fire-and-forget SAU commit → event handler fail thì mất, process crash thì mất |
| **Localized Fields** | INSERT chính xong rồi INSERT texts riêng → texts fail thì main đã commit |

```
HIỆN TẠI (MỖI BƯỚC LÀ CONNECTION + AUTO-COMMIT RIÊNG):

Step 1: conn₁ → INSERT nested_n1    → auto-commit ✓
Step 2: conn₂ → INSERT root         → auto-commit ✗ FAIL!
Step 3: conn₃ → INSERT nested_1n    → never reached
Step 4: conn₄ → INSERT texts        → never reached
Step 5: in-memory → PublishEvent     → never reached

Kết quả: nested_n1 đã persist nhưng root không tồn tại → DATA INCONSISTENCY
```

Nguyên nhân gốc: `ParameterizedQueryExecutor` mở connection mới cho mỗi operation, không có cơ chế truyền transaction xuyên suốt.

---

## 2. Event Taxonomy

Events in BMMDL should be classified into three categories:

### 2.1 System Events (Auto-generated)

Emitted automatically by the runtime for every CRUD operation. The user does not define them.

```
EntityCreated   → after successful INSERT
EntityUpdated   → after successful UPDATE (includes changed fields)
EntityDeleted   → after successful DELETE
```

**Naming convention**: `{EntityName}{Operation}` (e.g., `SalesOrderCreated`)

**Payload**: Full entity data + metadata (tenantId, userId, correlationId, timestamp).

These already work in the current implementation via `EventPublisher.PublishCreatedAsync/UpdatedAsync/DeletedAsync`.

### 2.2 Domain Events (User-defined)

Explicitly defined in the DSL with a typed schema. Represent meaningful business occurrences.

```bmmdl
event OrderStatusChanged {
    orderId: UUID;
    previousStatus: String(20);
    newStatus: String(20);
    changedBy: UUID;
    changedAt: DateTime;
}
```

**Emitted by**:
- `emit` statements inside rules or action bodies
- `emits` clause on actions (auto-emitted after action completes)

### 2.3 Integration Events (Future)

Cross-module or cross-system events for eventual consistency. Require durability guarantees (outbox pattern).

```bmmdl
@Integration
event InventoryReserved {
    orderId: UUID;
    warehouseId: UUID;
    items: Array<UUID>;
    reservedAt: DateTime;
}
```

---

## 3. Proposed Architecture

### 3.1 Layered Event Pipeline

```
                    ┌─────────────────────────────────┐
                    │       Event Producers            │
                    │  (CRUD, Rules, Actions, emit)    │
                    └──────────────┬──────────────────┘
                                   │
                                   ▼
                    ┌─────────────────────────────────┐
                    │       EventDispatcher            │
                    │  (replaces EventPublisher)       │
                    │                                  │
                    │  1. Validate event against schema│
                    │  2. Enrich with context metadata │
                    │  3. Route to appropriate channel │
                    └──────────────┬──────────────────┘
                                   │
                    ┌──────────────┼──────────────────┐
                    │              │                   │
                    ▼              ▼                   ▼
           ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐
           │  Sync Channel│ │ Async Channel│ │ Durable Channel  │
           │  (in-process)│ │ (in-process) │ │ (outbox → broker)│
           └──────┬───────┘ └──────┬───────┘ └──────┬───────────┘
                  │                │                 │
                  ▼                ▼                 ▼
           ┌──────────────┐ ┌──────────────┐ ┌──────────────────┐
           │ AuditLog     │ │ Service      │ │ Integration      │
           │ RealTime     │ │ Handlers     │ │ (Kafka/RabbitMQ) │
           │ MetricsCount │ │ Saga Steps   │ │ Webhooks         │
           └──────────────┘ └──────────────┘ └──────────────────┘
```

### 3.2 Core Interfaces

```csharp
/// Replaces IEventPublisher with richer contract.
public interface IEventDispatcher
{
    /// Dispatch a typed domain event.
    Task DispatchAsync(DomainEvent @event, EventOptions? options = null, CancellationToken ct = default);

    /// Dispatch a system CRUD event (auto-generated).
    Task DispatchSystemEventAsync(SystemEventType type, string entityName,
        Dictionary<string, object?> data, EventContext ctx, CancellationToken ct = default);
}

public enum SystemEventType { Created, Updated, Deleted }

public record EventOptions
{
    /// Deliver synchronously before returning response to client.
    public bool Synchronous { get; init; } = false;

    /// Require durable delivery (outbox pattern).
    public bool Durable { get; init; } = false;

    /// Delay delivery by this duration.
    public TimeSpan? Delay { get; init; }

    /// Priority (higher = processed first). Default = 0.
    public int Priority { get; init; } = 0;
}

public record EventContext
{
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }   // ID of the event/command that caused this event
    public string? SourceModule { get; init; }
}
```

### 3.3 DomainEvent (Enhanced)

```csharp
public record DomainEvent
{
    // Identity
    public Guid EventId { get; init; } = Guid.NewGuid();
    public required string EventName { get; init; }
    public int SchemaVersion { get; init; } = 1;

    // Subject
    public required string EntityName { get; init; }
    public Guid? EntityId { get; init; }

    // Payload
    public Dictionary<string, object?> Payload { get; init; } = new();

    // Context
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? SourceModule { get; init; }

    // Delivery
    public EventDeliveryStatus DeliveryStatus { get; set; } = EventDeliveryStatus.Pending;
    public int RetryCount { get; set; } = 0;
}

public enum EventDeliveryStatus { Pending, Delivered, Failed, DeadLettered }
```

---

## 4. DSL Design

### 4.1 Event Definition (existing, minimal changes)

```bmmdl
event OrderStatusChanged {
    orderId: UUID;
    previousStatus: String(20);
    newStatus: String(20);
    changedBy: UUID;
    changedAt: DateTime;
}
```

No changes needed. Already works in grammar and MetaModel.

### 4.2 Emitting Events from Rules

Current grammar already supports `emit` statements. Enhance with field mapping:

```bmmdl
rule OrderConfirmation for SalesOrder on after update {
    when status = 'Confirmed' and $old.status = 'Draft' {
        emit OrderStatusChanged with {
            orderId = ID;
            previousStatus = $old.status;
            newStatus = status;
            changedBy = $user.id;
            changedAt = $now;
        };
    }
}
```

**Grammar** (already exists in `BmmdlParser.g4`):
```antlr
emitStmt
    : EMIT identifierReference (WITH LBRACE emitField* RBRACE)? SEMICOLON
    ;

emitField
    : IDENTIFIER EQ expression SEMICOLON?
    ;
```

**Runtime integration needed**: The `RuleEngine` must handle `BmEmitStatement` by calling `IEventDispatcher.DispatchAsync()`.

### 4.3 Emitting Events from Actions (auto-emit)

```bmmdl
action confirm()
    requires status = 'Draft'
    ensures status = 'Confirmed'
    emits OrderStatusChanged;
```

The `emits` clause means: after the action completes successfully, the runtime auto-emits the named event with entity data as payload. The runtime populates event fields by matching field names to entity fields.

**Runtime integration needed**: `EntityActionController` / `ODataServiceController` must auto-emit events listed in `BmAction.Emits` after action execution.

### 4.4 Subscribing to Events in Services

```bmmdl
service NotificationService {
    on OrderStatusChanged {
        // $event.orderId, $event.newStatus, etc. are available
        when $event.newStatus = 'Shipped' {
            call sendShipmentNotification($event.orderId);
        }
    }

    on PaymentReceived {
        call updateAccountBalance($event.orderId, $event.amount);
    }
}
```

**Grammar addition needed** (not yet in parser):
```antlr
serviceEventHandler
    : ON identifierReference LBRACE ruleStmt* RBRACE
    ;
```

This maps to the existing `BmEventHandler` in MetaModel and `ServiceEventHandler` in runtime.

### 4.5 Entity-Level Event Subscription (Alternative syntax)

For simpler cases, allow subscribing directly on an entity:

```bmmdl
entity Inventory {
    key ID: UUID;
    productId: UUID;
    quantity: Integer;

    on OrderCreated {
        // Reduce inventory for each order item
        compute quantity = quantity - $event.orderQuantity;
    }
}
```

This is syntactic sugar that compiles to a rule with an event trigger.

---

## 5. Runtime Execution Design

### 5.1 Event Dispatch Flow

```
1. Producer calls IEventDispatcher.DispatchAsync(event)
2. EventDispatcher:
   a. Validate event payload against BmEvent schema (if typed)
   b. Enrich with EventContext (tenant, user, correlation, causation)
   c. Assign EventId + Timestamp
   d. If Durable → write to outbox table first
   e. Dispatch to registered handlers via IEventHandlerRegistry
3. Handler execution:
   a. AuditLogHandler     → always, async, non-blocking
   b. ServiceEventHandler → match by event name, execute statements
   c. RealtimeNotifier    → push to WebSocket/SignalR
   d. IntegrationHandler  → forward to external broker (future)
4. On handler failure:
   a. Log error
   b. Increment retry count
   c. If retries < maxRetries → schedule retry with backoff
   d. If retries exhausted → move to dead letter
```

### 5.2 Handler Registration

```csharp
public interface IEventHandlerRegistry
{
    void Register(IEventHandler handler);
    void RegisterForEvent(string eventName, Func<DomainEvent, CancellationToken, Task> handler);
    IReadOnlyList<IEventHandler> GetHandlersFor(string eventName);
}
```

Handlers are registered at startup via `EventHandlerRegistrationService` (already exists).

### 5.3 RuleEngine Integration

The `RuleEngine.ExecuteStatementAsync` must handle `BmEmitStatement`:

```csharp
case BmEmitStatement emit:
    var eventPayload = new Dictionary<string, object?>();
    foreach (var field in emit.Fields)
    {
        eventPayload[field.Name] = _evaluator.Evaluate(field.ExpressionAst, context);
    }
    var domainEvent = new DomainEvent
    {
        EventName = emit.EventName,
        EntityName = context.EntityName,
        Payload = eventPayload
        // Context filled by dispatcher
    };
    await _eventDispatcher.DispatchAsync(domainEvent, ct: ct);
    break;
```

### 5.4 Action Auto-Emit Integration

In `EntityActionController` after action execution:

```csharp
// After action body executes successfully
foreach (var eventName in action.Emits)
{
    var eventDef = _cache.GetEvent(eventName);
    var payload = BuildEventPayload(eventDef, entityData, actionResult);
    await _eventDispatcher.DispatchAsync(new DomainEvent
    {
        EventName = eventName,
        EntityName = entity.Name,
        EntityId = entityId,
        Payload = payload
    });
}
```

`BuildEventPayload` maps event fields to entity data by name matching:

```csharp
private Dictionary<string, object?> BuildEventPayload(
    BmEvent eventDef, Dictionary<string, object?> entityData, object? actionResult)
{
    var payload = new Dictionary<string, object?>();
    foreach (var field in eventDef.Fields)
    {
        // Try entity data first, then action result
        if (entityData.TryGetValue(field.Name, out var value))
            payload[field.Name] = value;
    }
    return payload;
}
```

---

## 6. Transaction Context Propagation

Đây là vấn đề kiến trúc cốt lõi: **làm sao để transaction context xuyên qua được event system?**

### 6.1 Vấn đề cần giải quyết

```
1. CRUD operation + Rules + Events phải nằm trong CÙNG MỘT transaction
2. Event chỉ được dispatch SAU KHI transaction commit thành công
3. Nếu transaction rollback → events không bao giờ được publish
4. Synchronous event handlers (cùng module) có thể tham gia transaction
5. Asynchronous event handlers (cross-module) chạy sau commit
```

### 6.2 Unit of Work Pattern

Giải pháp trung tâm là `IUnitOfWork` — một scoped service quản lý connection + transaction + pending events cho toàn bộ request lifecycle.

```csharp
/// <summary>
/// Scoped per-request. Owns the connection, transaction, and pending events.
/// All operations within a single HTTP request share this context.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// The active connection (reused for all operations in this request).
    NpgsqlConnection Connection { get; }

    /// The active transaction (null until BeginAsync called).
    NpgsqlTransaction? Transaction { get; }

    /// Tenant context for this request.
    Guid? TenantId { get; }

    /// Start a new transaction.
    Task BeginAsync(CancellationToken ct = default);

    /// Commit the transaction and dispatch all pending events.
    Task CommitAsync(CancellationToken ct = default);

    /// Rollback the transaction and discard all pending events.
    Task RollbackAsync(CancellationToken ct = default);

    /// Enqueue a domain event to be dispatched after commit.
    void EnqueueEvent(DomainEvent @event);

    /// Enqueue a durable event to be written to outbox within the transaction.
    void EnqueueDurableEvent(DomainEvent @event);

    /// Get all pending events (for testing/inspection).
    IReadOnlyList<DomainEvent> PendingEvents { get; }
}
```

**Implementation**:

```csharp
public class UnitOfWork : IUnitOfWork
{
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly IOutboxStore _outboxStore;

    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private readonly List<DomainEvent> _pendingEvents = new();
    private readonly List<DomainEvent> _durableEvents = new();
    private bool _committed = false;

    public NpgsqlConnection Connection =>
        _connection ?? throw new InvalidOperationException("UnitOfWork not started");
    public NpgsqlTransaction? Transaction => _transaction;
    public Guid? TenantId { get; }
    public IReadOnlyList<DomainEvent> PendingEvents => _pendingEvents.AsReadOnly();

    public UnitOfWork(
        ITenantConnectionFactory connectionFactory,
        IEventDispatcher eventDispatcher,
        IOutboxStore outboxStore,
        Guid? tenantId)
    {
        _connectionFactory = connectionFactory;
        _eventDispatcher = eventDispatcher;
        _outboxStore = outboxStore;
        TenantId = tenantId;
    }

    public async Task BeginAsync(CancellationToken ct = default)
    {
        _connection = await _connectionFactory.GetConnectionAsync(TenantId, ct);
        _transaction = await _connection.BeginTransactionAsync(ct);
    }

    public void EnqueueEvent(DomainEvent @event) => _pendingEvents.Add(@event);

    public void EnqueueDurableEvent(DomainEvent @event) => _durableEvents.Add(@event);

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction == null) throw new InvalidOperationException("No active transaction");

        // 1. Write durable events to outbox WITHIN the transaction
        foreach (var evt in _durableEvents)
        {
            await _outboxStore.EnqueueAsync(evt, _transaction, ct);
        }

        // 2. Commit everything atomically (data + outbox events)
        await _transaction.CommitAsync(ct);
        _committed = true;

        // 3. AFTER commit succeeds → dispatch in-memory events
        //    These are guaranteed to only fire if data was persisted.
        foreach (var evt in _pendingEvents)
        {
            await _eventDispatcher.DispatchAsync(evt, ct: ct);
        }

        // 4. Durable events will be picked up by OutboxProcessor
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(ct);
        }
        // Discard ALL pending events — nothing happened
        _pendingEvents.Clear();
        _durableEvents.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_committed && _transaction != null)
        {
            // Safety net: uncommitted transaction → rollback
            try { await _transaction.RollbackAsync(); } catch { /* already disposed */ }
        }
        if (_transaction != null) await _transaction.DisposeAsync();
        if (_connection != null) await _connection.DisposeAsync();
    }
}
```

### 6.3 How It Flows Through CRUD

```
┌──────────────────────────────────────────────────────────────────┐
│  HTTP Request (POST /api/odata/SalesOrder)                       │
│                                                                  │
│  IUnitOfWork uow = scope.Resolve<IUnitOfWork>();                │
│  await uow.BeginAsync();                     ──── SINGLE CONN  │
│                                                    SINGLE TXN   │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │ TRANSACTION BOUNDARY                                       │  │
│  │                                                            │  │
│  │  1. RuleEngine.ExecuteBeforeCreate(uow.Connection, uow.Transaction)    │
│  │     → validation, compute → uses SAME connection           │  │
│  │     → emit stmt? → uow.EnqueueEvent(event)                │  │
│  │                                                            │  │
│  │  2. DeepInsertHandler.Execute(uow.Connection, uow.Transaction)         │
│  │     → INSERT nested_n1 ... (same txn)                      │  │
│  │     → INSERT root ...       (same txn)                     │  │
│  │     → INSERT nested_1n ...  (same txn)                     │  │
│  │     → INSERT texts ...      (same txn)                     │  │
│  │     All or nothing!                                        │  │
│  │                                                            │  │
│  │  3. uow.EnqueueEvent(SalesOrderCreated { ... })           │  │
│  │                                                            │  │
│  │  4. Outbox: INSERT INTO event_outbox (same txn)            │  │
│  │     → for @Integration events only                         │  │
│  │                                                            │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  await uow.CommitAsync();                                        │
│  ┌─ DATA + OUTBOX committed atomically ─┐                       │
│  │                                       │                       │
│  │  POST-COMMIT:                         │                       │
│  │  5. RuleEngine.ExecuteAfterCreate()   │  ← separate, non-txn │
│  │  6. Dispatch in-memory events:        │                       │
│  │     → AuditLogHandler                 │                       │
│  │     → ServiceEventHandler             │                       │
│  │     → RealtimeNotifier                │                       │
│  └───────────────────────────────────────┘                       │
│                                                                  │
│  return 201 Created                                              │
└──────────────────────────────────────────────────────────────────┘

Nếu bất kỳ bước nào trong TRANSACTION BOUNDARY fail:
  → uow.RollbackAsync()
  → Toàn bộ INSERT bị rollback
  → Toàn bộ pending events bị discard
  → Không có event nào được publish
  → return 400/500
```

### 6.4 IQueryExecutor Refactor

`IQueryExecutor` cần overload nhận `NpgsqlConnection` + `NpgsqlTransaction` thay vì tự tạo connection:

```csharp
public interface IQueryExecutor
{
    // Existing methods (backward-compat, create own connection)
    Task<Dictionary<string, object?>?> ExecuteSingleAsync(
        string sql, IReadOnlyList<NpgsqlParameter>? parameters = null, CancellationToken ct = default);

    // NEW: Use provided connection/transaction from UnitOfWork
    Task<Dictionary<string, object?>?> ExecuteSingleAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string sql, IReadOnlyList<NpgsqlParameter>? parameters = null, CancellationToken ct = default);

    Task<int> ExecuteNonQueryAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string sql, IReadOnlyList<NpgsqlParameter>? parameters = null, CancellationToken ct = default);

    Task<Dictionary<string, object?>?> ExecuteReturningAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string sql, IReadOnlyList<NpgsqlParameter>? parameters = null, CancellationToken ct = default);
}
```

Cách triển khai gọn hơn: sử dụng overload mặc định rút từ UoW:

```csharp
public class ParameterizedQueryExecutor : IQueryExecutor
{
    private readonly IUnitOfWork? _unitOfWork;

    // When UoW is available, use its connection/transaction automatically
    public async Task<Dictionary<string, object?>?> ExecuteSingleAsync(
        string sql, IReadOnlyList<NpgsqlParameter>? parameters = null, CancellationToken ct = default)
    {
        if (_unitOfWork?.Transaction != null)
        {
            return await ExecuteSingleAsync(
                _unitOfWork.Connection, _unitOfWork.Transaction, sql, parameters, ct);
        }

        // Fallback: create own connection (for read-only queries, background jobs)
        await using var connection = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        // ... existing logic
    }
}
```

### 6.5 Event Flow Classification

Events cần được phân loại theo thời điểm dispatch:

```
┌──────────────────────────────────────────────────────────────┐
│                    EVENT DISPATCH TIMING                      │
├──────────────────┬───────────────────┬───────────────────────┤
│  PRE-COMMIT      │  AT-COMMIT        │  POST-COMMIT          │
│  (trong txn)     │  (cùng txn)       │  (sau commit)         │
├──────────────────┼───────────────────┼───────────────────────┤
│                  │ Outbox INSERT     │ In-memory dispatch    │
│  Synchronous     │ (durable events   │ (non-durable events   │
│  validation-     │  written to DB    │  dispatched to        │
│  style handlers  │  atomically with  │  handlers)            │
│  (future)        │  entity data)     │                       │
│                  │                   │ After rules           │
│  "before-emit"   │                   │ Audit log             │
│  rules           │                   │ Realtime notify       │
│                  │                   │ Service handlers      │
└──────────────────┴───────────────────┴───────────────────────┘
```

**Tại sao phải post-commit?**

Event handlers thường cần đọc data vừa được ghi. Nếu dispatch trong transaction:
- Handler đọc từ connection khác → chưa thấy data (READ COMMITTED isolation)
- Handler đọc từ cùng connection → phức tạp, dễ deadlock
- Handler fail → phải rollback cả CRUD operation → blast radius quá lớn

**Nguyên tắc**: CRUD + outbox write = atomic. Event dispatch = post-commit, best-effort (với retry cho durable events).

### 6.6 RuleEngine Integration with UoW

```csharp
public class RuleEngine : IRuleEngine
{
    private readonly IUnitOfWork _unitOfWork;

    // Before rules: run within transaction, can block commit
    public async Task<RuleExecutionResult> ExecuteBeforeCreateAsync(
        BmEntity entity, Dictionary<string, object?> data, EvaluationContext context)
    {
        var rules = GetRulesForTrigger(entity.QualifiedName, BmTriggerTiming.Before, BmTriggerOperation.Create);
        var result = await ExecuteRulesAsync(rules, data, context, "create");

        // If rules emit events, they go to UoW pending queue
        // They will only be dispatched if the commit succeeds
        return result;
    }

    private async Task<RuleExecutionResult> ExecuteStatementAsync(
        BmRuleStatement statement, EvaluationContext context)
    {
        // ...existing cases...

        case BmEmitStatement emit:
            var payload = new Dictionary<string, object?>();
            foreach (var field in emit.Fields)
            {
                payload[field.Name] = _evaluator.Evaluate(field.ExpressionAst, context);
            }
            var domainEvent = new DomainEvent
            {
                EventName = emit.EventName,
                EntityName = context.EntityData?["__entityName"]?.ToString() ?? "",
                Payload = payload,
                TenantId = context.TenantId,
                UserId = context.User?.UserId
            };

            // Enqueue, NOT dispatch — dispatched only after commit
            _unitOfWork.EnqueueEvent(domainEvent);
            break;
    }
}
```

### 6.7 DynamicEntityController with UoW

```csharp
public async Task<IActionResult> Create(
    string module, string entity,
    Dictionary<string, object?> data, CancellationToken ct)
{
    var uow = HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();

    await uow.BeginAsync(ct);
    try
    {
        // ── WITHIN TRANSACTION ──────────────────────────────────

        // 1. Before rules (validation, compute, emit)
        var ruleResult = await _ruleEngine.ExecuteBeforeCreateAsync(entityDef, data, evalContext);
        if (!ruleResult.Success) { await uow.RollbackAsync(ct); return BadRequest(...); }

        // 2. Apply computed values
        foreach (var (field, value) in ruleResult.ComputedValues)
            data[field] = value;

        // 3. INSERT (deep or simple) — uses uow.Connection + uow.Transaction
        Dictionary<string, object?> created;
        if (_deepInsertHandler.HasNestedObjects(entityDef, data))
            created = (await _deepInsertHandler.ExecuteAsync(entityDef, data, tenantId, ct)).RootEntity;
        else
        {
            var (sql, parameters) = _sqlBuilder.BuildInsertQuery(entityDef, data, tenantId);
            created = await _queryExecutor.ExecuteReturningAsync(sql, parameters, ct);
        }

        // 4. Enqueue system event (dispatch after commit)
        uow.EnqueueEvent(new DomainEvent
        {
            EventName = $"{entity}Created",
            EntityName = entity,
            EntityId = ExtractId(created),
            Payload = created,
            TenantId = tenantId,
            UserId = userId
        });

        // 5. If any emit from rules, they're already in uow.PendingEvents

        // ── COMMIT ──────────────────────────────────────────────
        await uow.CommitAsync(ct);
        // CommitAsync internally:
        //   a) Writes durable events to outbox (same txn)
        //   b) COMMIT
        //   c) Dispatches in-memory events to handlers
        //   d) Runs after-create rules

        return Created(...);
    }
    catch
    {
        await uow.RollbackAsync(ct);
        // All INSERTs rolled back, all events discarded
        throw;
    }
}
```

### 6.8 DeepInsertHandler with UoW

```csharp
public class DeepInsertHandler
{
    private readonly IUnitOfWork _unitOfWork;

    public async Task<DeepInsertResult> ExecuteAsync(
        BmEntity entityDef, Dictionary<string, object?> data,
        Guid? tenantId, CancellationToken ct)
    {
        // ALL inserts use the SAME connection + transaction from UoW

        // Step 1: N:1 nested entities
        foreach (var nested in nestedInserts.Where(n => n.Cardinality == BmCardinality.ManyToOne))
        {
            var (sql, parameters) = _sqlBuilder.BuildInsertQuery(nested.Entity, nested.Data, tenantId);
            var result = await _queryExecutor.ExecuteReturningAsync(
                _unitOfWork.Connection, _unitOfWork.Transaction,  // ← SAME TXN
                sql, parameters, ct);
            rootData[fkField] = result["id"];
        }

        // Step 2: Root entity
        var (rootSql, rootParams) = _sqlBuilder.BuildInsertQuery(entityDef, rootData, tenantId);
        var createdRoot = await _queryExecutor.ExecuteReturningAsync(
            _unitOfWork.Connection, _unitOfWork.Transaction,  // ← SAME TXN
            rootSql, rootParams, ct);

        // Step 3: 1:N nested entities
        foreach (var nested in nestedInserts.Where(n => n.Cardinality == BmCardinality.OneToMany))
        {
            // Each child INSERT uses SAME connection + transaction
            foreach (var childData in nested.Items)
            {
                childData[fkField] = rootId;
                var (sql, p) = _sqlBuilder.BuildInsertQuery(nested.Entity, childData, tenantId);
                await _queryExecutor.ExecuteReturningAsync(
                    _unitOfWork.Connection, _unitOfWork.Transaction,  // ← SAME TXN
                    sql, p, ct);
            }
        }

        // Nếu bất kỳ INSERT nào fail → exception propagates up
        // → DynamicEntityController catches → uow.RollbackAsync()
        // → TOÀN BỘ inserts (N:1 + root + 1:N) bị rollback
        // → Không có orphan data

        return new DeepInsertResult { ... };
    }
}
```

### 6.9 Event Handlers và Transaction Boundary

Event handlers **KHÔNG** tham gia transaction của CRUD operation. Chúng chạy post-commit:

```
┌────────────── CRUD Transaction ──────────────┐
│  Before Rules  →  INSERT/UPDATE  →  Outbox   │  ← atomic
└──────────────────────┬───────────────────────┘
                       │ COMMIT ✓
                       ▼
┌────────────── Post-Commit Phase ─────────────┐
│                                               │
│  Dispatch pending events:                     │
│                                               │
│  ┌─ AuditLogHandler ─────────────────────┐   │
│  │  INSERT INTO audit_logs (own txn)      │   │  ← separate txn
│  └────────────────────────────────────────┘   │
│                                               │
│  ┌─ ServiceEventHandler ─────────────────┐   │
│  │  on OrderCreated {                     │   │
│  │    compute inventory = ...             │   │  ← own UoW if writes
│  │    call sendNotification(...)          │   │
│  │  }                                     │   │
│  └────────────────────────────────────────┘   │
│                                               │
│  ┌─ RealtimeNotifier ────────────────────┐   │
│  │  SignalR broadcast                     │   │  ← no txn needed
│  └────────────────────────────────────────┘   │
│                                               │
│  Handler fail? → retry via outbox/dead letter │
│  Handler fail ≠ CRUD rollback                 │
└───────────────────────────────────────────────┘
```

**Tại sao handler không ở trong CRUD transaction?**

| Option | Pros | Cons |
|--------|------|------|
| **Handler TRONG txn** | Strong consistency | Deadlock risk, long-held locks, handler fail = CRUD fail, coupling |
| **Handler SAU commit** | Decoupled, no deadlock, handler fail isolated | Eventual consistency, handler sees committed data |

Chọn **handler sau commit** vì:
1. Handler có thể chậm (call external API, send email) → giữ txn lâu = lock contention
2. Handler fail không nên block user operation
3. Eventual consistency chấp nhận được cho hầu hết use cases
4. Nếu cần strong consistency → dùng `rule on before/after` thay vì event

### 6.10 Khi nào handler CẦN strong consistency?

Nếu business logic yêu cầu "Order created → Inventory MUST decrease atomically":

**Không dùng event** — dùng **rule**:
```bmmdl
rule DecreaseInventory for OrderItem on after create {
    // Runs within same transaction as OrderItem INSERT
    compute product.stockQuantity = product.stockQuantity - quantity;
}
```

**Dùng event** khi chấp nhận eventual consistency:
```bmmdl
service InventoryService {
    on OrderCreated {
        // Runs post-commit, may fail independently
        call reserveStock($event.orderId);
    }
}
```

### 6.11 Transaction Context Diagram (Full Picture)

```
                         ┌─────────────────────┐
                         │   HTTP Request       │
                         │   (Scoped DI)        │
                         └──────────┬──────────┘
                                    │
                         ┌──────────▼──────────┐
                         │    IUnitOfWork       │
                         │  ┌───────────────┐   │
                         │  │ NpgsqlConn    │   │  ← single connection
                         │  │ NpgsqlTxn     │   │  ← single transaction
                         │  │ PendingEvents │   │  ← event buffer
                         │  │ DurableEvents │   │  ← outbox buffer
                         │  └───────┬───────┘   │
                         └──────────┼──────────┘
                                    │
            ┌───────────────────────┼───────────────────────┐
            │                       │                       │
   ┌────────▼─────────┐   ┌───────▼────────┐   ┌─────────▼─────────┐
   │   RuleEngine      │   │ QueryExecutor  │   │ DeepInsertHandler │
   │                   │   │                │   │                   │
   │ Uses UoW.Conn     │   │ Uses UoW.Conn  │   │ Uses UoW.Conn    │
   │ Uses UoW.Txn      │   │ Uses UoW.Txn   │   │ Uses UoW.Txn     │
   │                   │   │                │   │                   │
   │ emit stmt →       │   │ INSERT/UPDATE  │   │ INSERT n:1       │
   │  uow.Enqueue()   │   │ DELETE/SELECT  │   │ INSERT root      │
   │                   │   │                │   │ INSERT 1:n       │
   └───────────────────┘   └────────────────┘   └───────────────────┘
            │                       │                       │
            └───────────────────────┼───────────────────────┘
                                    │
                              ALL SHARE THE SAME
                              CONNECTION + TRANSACTION
                                    │
                         ┌──────────▼──────────┐
                         │  uow.CommitAsync()   │
                         │                      │
                         │  1. Outbox INSERT    │ ← still in txn
                         │  2. COMMIT           │ ← atomic
                         │  3. Dispatch events  │ ← post-commit
                         └──────────────────────┘
```

### 6.12 DI Registration

```csharp
// Program.cs
builder.Services.AddScoped<IUnitOfWork>(sp =>
{
    var connectionFactory = sp.GetRequiredService<ITenantConnectionFactory>();
    var eventDispatcher = sp.GetRequiredService<IEventDispatcher>();
    var outboxStore = sp.GetRequiredService<IOutboxStore>();
    var httpContext = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
    var tenantId = ExtractTenantId(httpContext);

    return new UnitOfWork(connectionFactory, eventDispatcher, outboxStore, tenantId);
});

// QueryExecutor now receives UoW
builder.Services.AddScoped<IQueryExecutor>(sp =>
{
    var uow = sp.GetRequiredService<IUnitOfWork>();
    var connectionFactory = sp.GetRequiredService<ITenantConnectionFactory>();
    return new ParameterizedQueryExecutor(connectionFactory, uow);
});
```

### 6.13 Migration Path (từ code hiện tại)

```
Phase A: Introduce IUnitOfWork interface + UnitOfWork class
         ├── Không thay đổi existing code
         └── UoW available nhưng chưa ai dùng

Phase B: ParameterizedQueryExecutor nhận optional IUnitOfWork
         ├── Nếu UoW active → dùng UoW.Connection + UoW.Transaction
         └── Nếu không → fallback tạo connection riêng (backward-compat)

Phase C: DynamicEntityController wrap operations trong UoW
         ├── Begin → Before Rules → CRUD → Enqueue Events → Commit
         └── Catch → Rollback

Phase D: DeepInsertHandler / DeepUpdateHandler dùng UoW
         ├── Không tự tạo connection nữa
         └── Tất cả INSERT/UPDATE trong cùng transaction

Phase E: Event dispatch chuyển sang post-commit
         ├── EnqueueEvent thay cho PublishAsync trong transaction
         └── CommitAsync dispatch events
```

---

## 7. Durability: Outbox Pattern (integrated with UoW)

For integration events that must not be lost:

### 6.1 Outbox Table

```sql
CREATE TABLE platform.event_outbox (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_name      VARCHAR(255) NOT NULL,
    entity_name     VARCHAR(255) NOT NULL,
    entity_id       UUID,
    tenant_id       UUID,
    payload         JSONB NOT NULL,
    context         JSONB,          -- correlation, causation, user, module
    schema_version  INTEGER NOT NULL DEFAULT 1,
    status          VARCHAR(20) NOT NULL DEFAULT 'pending',
                    -- pending, processing, delivered, failed, dead_lettered
    retry_count     INTEGER NOT NULL DEFAULT 0,
    max_retries     INTEGER NOT NULL DEFAULT 5,
    next_retry_at   TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at    TIMESTAMPTZ,
    error_message   TEXT
);

CREATE INDEX idx_outbox_status ON platform.event_outbox(status, next_retry_at);
CREATE INDEX idx_outbox_entity ON platform.event_outbox(entity_name, entity_id);
```

### 6.2 Outbox Processor

A background service polls the outbox and dispatches pending events:

```csharp
public class OutboxProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batch = await _outboxStore.GetPendingAsync(batchSize: 100, ct);
            foreach (var entry in batch)
            {
                try
                {
                    await _eventDispatcher.DispatchAsync(entry.ToDomainEvent(), ct: ct);
                    await _outboxStore.MarkDeliveredAsync(entry.Id, ct);
                }
                catch (Exception ex)
                {
                    await _outboxStore.MarkFailedAsync(entry.Id, ex.Message, ct);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
```

### 6.3 Transactional Write

For durable events, the outbox write happens in the same DB transaction as the entity change:

```csharp
await using var transaction = await connection.BeginTransactionAsync(ct);
try
{
    // 1. Perform entity CRUD
    await _repository.CreateAsync(entity, data, transaction, ct);

    // 2. Write event to outbox (same transaction)
    await _outboxStore.EnqueueAsync(domainEvent, transaction, ct);

    await transaction.CommitAsync(ct);
}
catch
{
    await transaction.RollbackAsync(ct);
    throw;
}
```

---

## 8. Event Schema Validation

Since domain events have typed schemas (`BmEvent` with `BmEventField`), validate payloads at dispatch time:

```csharp
public class EventSchemaValidator
{
    private readonly MetaModelCache _cache;

    public EventValidationResult Validate(string eventName, Dictionary<string, object?> payload)
    {
        var eventDef = _cache.GetEvent(eventName);
        if (eventDef == null)
            return EventValidationResult.Success(); // Untyped event, allow

        var errors = new List<string>();
        foreach (var field in eventDef.Fields)
        {
            if (!payload.ContainsKey(field.Name))
            {
                errors.Add($"Missing required field: {field.Name}");
            }
            // Type validation against field.TypeRef
        }
        return errors.Count > 0
            ? EventValidationResult.Failed(errors)
            : EventValidationResult.Success();
    }
}
```

---

## 9. Event Versioning

### 8.1 Schema Version in Event Definition

```bmmdl
@Version(2)
event OrderStatusChanged {
    orderId: UUID;
    previousStatus: String(20);
    newStatus: String(20);
    changedBy: UUID;
    changedAt: DateTime;
    // v2: added reason field
    reason: String(500);
}
```

### 8.2 Version Compatibility

- **Backward compatible**: New optional fields only (additive)
- **Breaking change**: New major version, old handlers receive v1 projection
- Version stored in `DomainEvent.SchemaVersion`
- Handlers can declare which versions they support

---

## 10. Event Observability

### 9.1 Metrics

```
bmmdl_events_published_total{event_name, entity_name, tenant}
bmmdl_events_handled_total{event_name, handler, status}
bmmdl_events_handler_duration_seconds{event_name, handler}
bmmdl_events_outbox_pending_count
bmmdl_events_dead_letter_count{event_name}
```

### 9.2 Tracing

Each event carries `CorrelationId` (request-level) and `CausationId` (event that caused this event):

```
Request-123 (CorrelationId)
  → CreateOrder      (CausationId: null)
    → OrderCreated   (CausationId: CreateOrder)
      → InventoryReserved (CausationId: OrderCreated)
      → NotificationSent  (CausationId: OrderCreated)
```

### 9.3 Audit API

Existing `AuditLogStore` already provides query capabilities. Extend with:

```
GET /api/admin/events?entity=SalesOrder&from=2024-01-01&type=OrderStatusChanged
GET /api/admin/events/{eventId}/chain   → returns causal chain via correlationId
```

---

## 11. DSL Syntax Summary

```bmmdl
// ─── Define events ───
event OrderStatusChanged {
    orderId: UUID;
    previousStatus: String(20);
    newStatus: String(20);
    changedBy: UUID;
    changedAt: DateTime;
}

// ─── Emit from rules ───
rule TrackStatusChange for SalesOrder on after update {
    when status != $old.status {
        emit OrderStatusChanged with {
            orderId = ID;
            previousStatus = $old.status;
            newStatus = status;
            changedBy = $user.id;
            changedAt = $now;
        };
    }
}

// ─── Auto-emit from actions ───
entity SalesOrder {
    action confirm()
        requires status = 'Draft'
        ensures status = 'Confirmed'
        emits OrderStatusChanged;
}

// ─── Handle in services ───
service NotificationService {
    on OrderStatusChanged {
        when $event.newStatus = 'Shipped' {
            call sendEmail($event.orderId, 'Your order has shipped!');
        }
    }
}

// ─── Handle on entities ───
entity Inventory {
    on OrderCreated {
        compute reserved = reserved + $event.quantity;
    }
}

// ─── Integration event ───
@Integration
@Version(1)
event InventoryReserved {
    orderId: UUID;
    warehouseId: UUID;
    reservedAt: DateTime;
}
```

---

## 12. Implementation Phases

### ~~Phase 0: Unit of Work + Transaction Context (PREREQUISITE)~~ — DONE

**Goal**: Fix the fundamental transaction safety issue before building event system on top.

~~1. Introduce `IUnitOfWork` interface + `UnitOfWork` class~~
~~2. Add overloads to `IQueryExecutor` accepting `NpgsqlConnection` + `NpgsqlTransaction`~~
~~3. Wrap `DynamicEntityController` CRUD operations in UoW~~
~~4. Pass UoW through `DeepInsertHandler` / `DeepUpdateHandler`~~
~~5. Move event publish to post-commit via `uow.EnqueueEvent()`~~

**Implemented** (2026-02-15):
- `IUnitOfWork` + `UnitOfWork`: Scoped per-request, owns connection + transaction + pending events. Auto-rollback on dispose.
- `ParameterizedQueryExecutor`: Transparently uses UoW connection/transaction when `IsStarted == true`, falls back to standalone connections for reads.
- All 10 runtime controllers wrapped: DynamicEntityController, BatchController, BulkImportController, FileStorageController, EntityActionController, EntityReferenceController, EntityNavigationController, ODataServiceController, DynamicServiceController, DynamicViewController.
- Deep handlers: Verified transparent UoW participation via shared scoped `IQueryExecutor`.
- RuleEngine: `emit` statements enqueue events via `IUnitOfWork.EnqueueEvent()` when UoW active, fallback to direct dispatch.
- Event dispatch: Post-commit only — events dispatched by `UoW.CommitAsync()` after successful transaction commit. Rollback discards all pending events.
- DI: `IUnitOfWork` registered as Scoped, `ParameterizedQueryExecutor` receives UoW via constructor.
- Tests: 29 UnitOfWork lifecycle tests, 8 QueryExecutor UoW tests, 7 RuleEngine emit tests, 9 deep handler tests, 8 E2E transaction safety tests (61 total).

### ~~Phase 1: Complete emit/emits Runtime Wiring~~ — DONE

**Goal**: ~~Make existing grammar features actually work end-to-end.~~ **COMPLETE**

1. ~~Handle `BmEmitStatement` in `RuleEngine.ExecuteStatementAsync`~~ — Done (Events P0)
2. ~~Wire `BmAction.Emits` auto-emission in `EntityActionController`~~ — Done (Events P0)
3. ~~Add `GetEvent(string name)` to `MetaModelCache`~~ — Done: `_eventsByName` index + `GetEvent/HasEvent/AddEvent/Events/EventNames`
4. ~~Add event schema validation on dispatch~~ — Done: `EventSchemaValidator` with `Validate()` + `BuildSchemaPayload()`, advisory validation in `EventPublisher.PublishAsync()`
5. ~~Emit uses `uow.EnqueueEvent()` instead of direct dispatch~~ — Done (Events P0)

**Implementation details**:
- `EventSchemaValidator`: validates payload against `BmEvent.Fields` (required field checks, type compatibility). Advisory only — logs warnings but never blocks dispatch.
- `EventPublisher`: accepts optional `EventSchemaValidator` + `Func<string, BmEvent?>` lookup. Backward-compatible constructor.
- `MetaModelCache`: indexes `model.Events` by name (case-insensitive), exposes `GetEvent()`.
- Tests: 65 new tests (11 cache + 17 validator + 37 pipeline).

### Phase 2: Service Event Handlers Grammar — **DONE**

**Goal**: Parse and execute `on EventName { ... }` blocks in services.

> **Implementation status**: COMPLETE. Grammar `serviceEventHandler : ON identifierReference LBRACE actionStmt* RBRACE` added to `serviceElement`. ModelBuilder `VisitServiceEventHandler` populates `BmService.EventHandlers`. Runtime `ServiceEventHandler` + `EventHandlerRegistrationService` were already implemented. 25 tests.

1. Add `serviceEventHandler` rule to `BmmdlParser.g4`
2. Build `BmEventHandler` in `BmmdlModelBuilder.cs`
3. Verify `ServiceEventHandler.cs` execution path

**Files to modify**:
- `Grammar/BmmdlParser.g4`
- `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs`

### Phase 3: Durability (Outbox) — **DONE**

**Goal**: Events survive process crashes.

> **Implementation status**: COMPLETE. `IOutboxStore`/`OutboxStore` (Npgsql, transactional INSERT), `OutboxProcessor` (polling processor), `OutboxProcessorService` (BackgroundService), `event_outbox` DDL in SchemaManager, DI registration in Program.cs. Exponential backoff retry, dead letter handling. 17 tests.

1. Create `event_outbox` table in `SchemaManager`
2. Implement `OutboxStore`
3. Implement `OutboxProcessor` background service
4. Add `EventOptions.Durable` flag to dispatch

**New files**:
- `src/BMMDL.Runtime/Events/OutboxStore.cs`
- `src/BMMDL.Runtime/Events/OutboxProcessor.cs`

### Phase 4: Retry & Dead Letter

**Goal**: Failed handlers get retried; permanently failed events are quarantined.

1. Add retry logic with exponential backoff to handler execution
2. Add dead letter table/store
3. Add admin API for dead letter inspection and replay

### Phase 5: Observability & Tracing — **DONE** ✓

**Goal**: Full visibility into event flow.

1. ~~Add `CausationId` tracking through event chains~~ — **DONE**: DomainEvent.EventId/CausationId/SchemaVersion/SourceModule, CorrelationId propagation from HttpContext, UoW auto-sets CorrelationId on enqueued events
2. ~~Add metrics counters~~ — **DONE**: IEventMetrics interface, BmmdlMetrics implements 5 counters (events_published, events_handled, handler_duration, outbox_pending, dead_letter), OutboxProcessor records dead letter metrics
3. ~~Add event chain API endpoint~~ — **DONE**: AuditLogStore.QueryByCorrelationIdAsync, AuditLogController GET /api/admin/events/{correlationId}/chain, causation_id column + indexes on audit_logs table

### Phase 6: Integration Events — **DONE** ✓

**Goal**: Cross-system event delivery.

1. Add `@Integration` annotation support
2. Implement message broker adapter (RabbitMQ or Kafka)
3. Add webhook dispatch handler

---

## 13. Comparison with SAP CDS / CAP Events

BMMDL events are inspired by [SAP CAP Events](https://cap.cloud.sap/docs/guides/messaging/):

| Feature | SAP CAP | BMMDL (Proposed) |
|---------|---------|------------------|
| Auto CRUD events | `srv.after('CREATE', ...)` | `EntityCreated` auto-published |
| Custom events | `event OrderShipped { ... }` | `event OrderShipped { ... }` |
| Emit syntax | `req.emit('OrderShipped', data)` | `emit OrderShipped with { ... };` |
| Service handlers | `srv.on('OrderShipped', ...)` | `on OrderShipped { ... }` |
| Messaging broker | SAP Event Mesh / Kafka | Outbox + Broker adapter |
| Durability | Outbox with SAP HANA | Outbox with PostgreSQL |

Key difference: BMMDL events are **declarative** (defined in DSL, compiled to metadata), while CAP events are **imperative** (defined in JavaScript/Java handlers).

---

## 14. Open Questions

1. **Should system CRUD events be suppressible?** e.g., `@SuppressEvents` on an entity to skip auto-publishing for high-throughput batch operations.

2. **Event filtering at subscription level?** e.g., `on OrderStatusChanged where newStatus = 'Cancelled' { ... }` to avoid handler-side filtering.

3. **Transactional event handlers?** Should service event handlers execute within the same DB transaction as the triggering CRUD operation, or always asynchronously?

4. **Event replay?** Should we support replaying historical events from audit log for debugging or re-processing?

5. **Cross-module event visibility?** If module A defines `event X`, can module B's service subscribe to it? What about namespace scoping?
