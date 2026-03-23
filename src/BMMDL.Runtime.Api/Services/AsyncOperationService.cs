namespace BMMDL.Runtime.Api.Services;

using System.Collections.Concurrent;

/// <summary>
/// Service for tracking async OData operations.
/// Supports OData v4 Prefer: respond-async pattern.
/// Uses in-memory storage - production should use Redis/DB.
/// </summary>
public class AsyncOperationService : IAsyncOperationService
{
    private readonly ConcurrentDictionary<Guid, AsyncOperationStatus> _operations = new();
    private readonly ILogger<AsyncOperationService> _logger;
    private long _lastCleanupTicks = DateTimeOffset.UtcNow.Ticks;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(4);

    public AsyncOperationService(ILogger<AsyncOperationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create a new async operation and return its ID.
    /// </summary>
    public Guid CreateOperation(string operationType, Guid? entityId = null, Dictionary<string, object?>? metadata = null, Guid? tenantId = null)
    {
        var operationId = Guid.NewGuid();
        var operation = new AsyncOperationStatus
        {
            OperationId = operationId,
            OperationType = operationType,
            EntityId = entityId,
            TenantId = tenantId ?? Guid.Empty,
            Status = OperationState.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            Metadata = metadata ?? new Dictionary<string, object?>()
        };

        _operations[operationId] = operation;
        _logger.LogInformation("Created async operation {OperationId} of type {Type}", operationId, operationType);

        return operationId;
    }

    /// <summary>
    /// Get the status of an operation. Also performs lazy cleanup of expired operations.
    /// </summary>
    public AsyncOperationStatus? GetOperation(Guid operationId)
    {
        // Lazy cleanup: periodically remove completed operations older than DefaultMaxAge
        var now = DateTimeOffset.UtcNow;
        var lastTicks = Interlocked.Read(ref _lastCleanupTicks);
        if (now.Ticks - lastTicks > CleanupInterval.Ticks)
        {
            // Atomically update _lastCleanupTicks only if it hasn't been changed by another thread
            if (Interlocked.CompareExchange(ref _lastCleanupTicks, now.Ticks, lastTicks) == lastTicks)
            {
                CleanupOldOperations(DefaultMaxAge);
            }
        }

        return _operations.TryGetValue(operationId, out var op) ? op : null;
    }

    /// <summary>
    /// Mark operation as completed with result.
    /// </summary>
    public void CompleteOperation(Guid operationId, object? result = null)
    {
        if (_operations.TryGetValue(operationId, out var op))
        {
            op.Status = OperationState.Succeeded;
            op.CompletedAt = DateTimeOffset.UtcNow;
            op.Result = result;
            _logger.LogInformation("Completed async operation {OperationId}", operationId);
        }
    }

    /// <summary>
    /// Mark operation as failed with error.
    /// </summary>
    public void FailOperation(Guid operationId, string error)
    {
        if (_operations.TryGetValue(operationId, out var op))
        {
            op.Status = OperationState.Failed;
            op.CompletedAt = DateTimeOffset.UtcNow;
            op.Error = error;
            _logger.LogWarning("Failed async operation {OperationId}: {Error}", operationId, error);
        }
    }

    /// <summary>
    /// Update operation progress (0-100).
    /// </summary>
    public void UpdateProgress(Guid operationId, int percentComplete)
    {
        if (_operations.TryGetValue(operationId, out var op))
        {
            op.PercentComplete = Math.Clamp(percentComplete, 0, 100);
        }
    }

    /// <summary>
    /// Clean up old completed operations (call periodically).
    /// </summary>
    public void CleanupOldOperations(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var toRemove = _operations
            .Where(kv => kv.Value.CompletedAt.HasValue && kv.Value.CompletedAt < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in toRemove)
        {
            _operations.TryRemove(id, out _);
        }

        if (toRemove.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old async operations", toRemove.Count);
        }
    }
}

/// <summary>
/// Interface for async operation tracking.
/// </summary>
public interface IAsyncOperationService
{
    Guid CreateOperation(string operationType, Guid? entityId = null, Dictionary<string, object?>? metadata = null, Guid? tenantId = null);
    AsyncOperationStatus? GetOperation(Guid operationId);
    void CompleteOperation(Guid operationId, object? result = null);
    void FailOperation(Guid operationId, string error);
    void UpdateProgress(Guid operationId, int percentComplete);
    void CleanupOldOperations(TimeSpan maxAge);
}

/// <summary>
/// Status of an async operation.
/// </summary>
public class AsyncOperationStatus
{
    public Guid OperationId { get; set; }
    public string OperationType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public Guid TenantId { get; set; }
    public OperationState Status { get; set; }
    public int PercentComplete { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

/// <summary>
/// State of an async operation.
/// </summary>
public enum OperationState
{
    Running,
    Succeeded,
    Failed
}
