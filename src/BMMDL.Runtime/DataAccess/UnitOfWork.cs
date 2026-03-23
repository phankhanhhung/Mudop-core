using Microsoft.Extensions.Logging;
using Npgsql;
using BMMDL.Runtime.Events;

namespace BMMDL.Runtime.DataAccess;

/// <summary>
/// Unit of Work implementation that manages a single database connection and transaction
/// for the duration of a write operation. Ensures all DB operations within a request
/// are atomic — either all succeed or all are rolled back.
///
/// Post-commit event dispatch: events enqueued via EnqueueEvent are only dispatched
/// after the transaction commits successfully. If the transaction rolls back, events are discarded.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<UnitOfWork> _logger;
    private readonly IOutboxStore? _outboxStore;

    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private readonly List<DomainEvent> _pendingEvents = new();
    private readonly List<DomainEvent> _pendingDurableEvents = new();
    private bool _committed;
    private bool _rolledBack;
    private bool _disposed;

    public NpgsqlConnection Connection =>
        _connection ?? throw new InvalidOperationException("UnitOfWork not started. Call BeginAsync first.");

    public NpgsqlTransaction? Transaction => _transaction;
    public bool IsStarted => _connection != null && _transaction != null;
    public bool IsCompleted => _committed || _rolledBack;
    public Guid? TenantId { get; }
    public string? CorrelationId { get; set; }
    public IReadOnlyList<DomainEvent> PendingEvents => _pendingEvents.AsReadOnly();
    public IReadOnlyList<DomainEvent> PendingDurableEvents => _pendingDurableEvents.AsReadOnly();

    public UnitOfWork(
        ITenantConnectionFactory connectionFactory,
        IEventPublisher eventPublisher,
        ILogger<UnitOfWork> logger,
        Guid? tenantId,
        IOutboxStore? outboxStore = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TenantId = tenantId;
        _outboxStore = outboxStore;
    }

    public async Task BeginAsync(CancellationToken ct = default)
    {
        if (_connection != null)
            throw new InvalidOperationException("UnitOfWork already started.");

        _connection = await _connectionFactory.GetConnectionAsync(TenantId, ct);
        _transaction = await _connection.BeginTransactionAsync(ct);
        _logger.LogDebug("UnitOfWork started (TenantId: {TenantId})", TenantId);
    }

    public void EnqueueEvent(DomainEvent @event)
    {
        // Stamp CorrelationId from UoW context if not already set on the event
        if (@event.CorrelationId == null && CorrelationId != null)
        {
            @event = @event with { CorrelationId = CorrelationId };
        }
        _pendingEvents.Add(@event);
    }

    public void EnqueueDurableEvent(DomainEvent @event)
    {
        // Stamp CorrelationId from UoW context if not already set on the event
        if (@event.CorrelationId == null && CorrelationId != null)
        {
            @event = @event with { CorrelationId = CorrelationId };
        }
        _pendingDurableEvents.Add(@event);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");

        // 1. BEFORE commit: write durable events to outbox (same transaction = atomic)
        if (_pendingDurableEvents.Count > 0 && _outboxStore != null)
        {
            foreach (var evt in _pendingDurableEvents)
            {
                await _outboxStore.EnqueueAsync(evt, _transaction, isIntegration: true, ct);
            }
            _logger.LogDebug("Wrote {Count} durable events to outbox (pre-commit)", _pendingDurableEvents.Count);
        }
        else if (_pendingDurableEvents.Count > 0 && _outboxStore == null)
        {
            _logger.LogWarning("UnitOfWork has {Count} durable events but no IOutboxStore configured — events will be lost",
                _pendingDurableEvents.Count);
        }

        // 2. Commit the transaction (all DB operations + outbox entries become atomic)
        await _transaction.CommitAsync(ct);
        _committed = true; // Set immediately after commit, before event dispatch
        _logger.LogDebug("UnitOfWork committed ({EventCount} in-memory + {DurableCount} durable events)",
            _pendingEvents.Count, _pendingDurableEvents.Count);

        // 3. AFTER commit succeeds -> dispatch in-memory events (post-commit, best-effort)
        //    Events are guaranteed to only fire if data was persisted.
        //    Pass the full DomainEvent to preserve tracing fields (EventId, CausationId, etc.)
        //    Entire dispatch is wrapped in try-catch — transaction is already committed.
        try
        {
            foreach (var evt in _pendingEvents)
            {
                try
                {
                    await _eventPublisher.PublishAsync(evt, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch post-commit event {EventName}", evt.EventName);
                    // Don't throw — data is committed, event dispatch is best-effort
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch {Count} post-commit events", _pendingEvents.Count);
            // Don't rethrow — transaction is already committed
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            try
            {
                await _transaction.RollbackAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during UnitOfWork rollback");
            }
        }
        _rolledBack = true;
        // Discard ALL pending events — nothing happened
        _pendingEvents.Clear();
        _pendingDurableEvents.Clear();
        _logger.LogDebug("UnitOfWork rolled back, events discarded");
    }

    public async Task SavepointAsync(string name, CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction for savepoint.");
        await _transaction.SaveAsync(name, ct);
    }

    public async Task RollbackToSavepointAsync(string name, CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction for savepoint rollback.");
        await _transaction.RollbackAsync(name, ct);
    }

    public async Task ReleaseSavepointAsync(string name, CancellationToken ct = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction for savepoint release.");
        await _transaction.ReleaseAsync(name, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_committed && _transaction != null)
        {
            // Safety net: uncommitted transaction -> auto-rollback
            try
            {
                await _transaction.RollbackAsync();
                _logger.LogWarning("UnitOfWork disposed without commit — auto-rollback performed");
            }
            catch { /* already disposed or rolled back */ }
        }

        if (_transaction != null)
            await _transaction.DisposeAsync();
        if (_connection != null)
            await _connection.DisposeAsync();

        _pendingEvents.Clear();
        _pendingDurableEvents.Clear();
    }
}
