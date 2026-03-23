using Npgsql;
using BMMDL.Runtime.Events;

namespace BMMDL.Runtime.DataAccess;

/// <summary>
/// Scoped per-request. Owns the connection, transaction, and pending events.
/// All write operations within a single HTTP request share this context.
/// Read-only operations (GET) do NOT require UoW — they use standalone connections.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// The active connection (reused for all operations in this request).
    /// Throws if BeginAsync has not been called.
    /// </summary>
    NpgsqlConnection Connection { get; }

    /// <summary>
    /// The active transaction (null until BeginAsync called).
    /// </summary>
    NpgsqlTransaction? Transaction { get; }

    /// <summary>
    /// Whether the UoW has been started (BeginAsync called, transaction active).
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// Whether the UoW has been completed (committed or rolled back).
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Tenant context for this request.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// Correlation ID for tracing events across the request lifecycle.
    /// Set from HttpContext.TraceIdentifier or X-Correlation-Id header.
    /// </summary>
    string? CorrelationId { get; set; }

    /// <summary>
    /// Start a new database connection and transaction.
    /// All subsequent IQueryExecutor calls within this scope will use this connection/transaction.
    /// </summary>
    Task BeginAsync(CancellationToken ct = default);

    /// <summary>
    /// Commit the transaction atomically, then dispatch all pending events post-commit.
    /// Events are only dispatched if the commit succeeds.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Rollback the transaction and discard all pending events.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>
    /// Enqueue a domain event to be dispatched after successful commit.
    /// If the transaction is rolled back, the event is discarded.
    /// </summary>
    void EnqueueEvent(DomainEvent @event);

    /// <summary>
    /// Enqueue an integration event for durable delivery via the outbox.
    /// Written to the outbox table within the same transaction as entity CRUD (atomic).
    /// If the transaction is rolled back, the outbox entry is also rolled back.
    /// </summary>
    void EnqueueDurableEvent(DomainEvent @event);

    /// <summary>
    /// Get all pending events (for testing/inspection).
    /// </summary>
    IReadOnlyList<DomainEvent> PendingEvents { get; }

    /// <summary>
    /// Get all pending durable events (for testing/inspection).
    /// </summary>
    IReadOnlyList<DomainEvent> PendingDurableEvents { get; }

    /// <summary>
    /// Create a savepoint within the current transaction.
    /// Used for batch operations where individual writes should be isolated.
    /// </summary>
    Task SavepointAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Rollback to a previously created savepoint.
    /// The transaction remains valid for subsequent operations.
    /// </summary>
    Task RollbackToSavepointAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Release a savepoint (cleanup, no rollback).
    /// </summary>
    Task ReleaseSavepointAsync(string name, CancellationToken ct = default);
}
