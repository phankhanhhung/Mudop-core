namespace BMMDL.Runtime.Events;

using BMMDL.MetaModel;
using BMMDL.Runtime.Extensions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Interface for publishing domain events.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish an event asynchronously by name and payload.
    /// </summary>
    Task PublishAsync(string eventName, Dictionary<string, object?> payload, CancellationToken ct = default);

    /// <summary>
    /// Publish a fully constructed domain event, preserving all tracing fields.
    /// </summary>
    Task PublishAsync(DomainEvent @event, CancellationToken ct = default);

    /// <summary>
    /// Publish entity created event.
    /// </summary>
    Task PublishCreatedAsync(string entityName, Dictionary<string, object?> data, CancellationToken ct = default);

    /// <summary>
    /// Publish entity updated event.
    /// </summary>
    Task PublishUpdatedAsync(string entityName, Dictionary<string, object?> data,
        Dictionary<string, object?>? changedFields = null, CancellationToken ct = default);

    /// <summary>
    /// Publish entity deleted event.
    /// </summary>
    Task PublishDeletedAsync(string entityName, Guid entityId, CancellationToken ct = default);
}

/// <summary>
/// Domain event data.
/// </summary>
public record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public required string EventName { get; init; }
    public required string EntityName { get; init; }
    public Guid? EntityId { get; init; }
    public Dictionary<string, object?> Payload { get; init; } = new();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public string? SourceModule { get; init; }
}

/// <summary>
/// Simple in-memory event publisher.
/// In production, this would publish to a message broker (RabbitMQ, Kafka, etc.)
/// </summary>
public class EventPublisher : IEventPublisher
{
    private readonly ILogger<EventPublisher> _logger;
    private readonly IRealtimeNotifier? _notifier;
    private readonly EventSchemaValidator? _validator;
    private readonly Func<string, BmEvent?>? _eventLookup;
    private readonly IEventMetrics? _metrics;
    // Thread-safe collection for concurrent handler registration and iteration
    private readonly System.Collections.Concurrent.ConcurrentBag<IEventHandler> _handlers = new();

    public EventPublisher(ILogger<EventPublisher> logger, IRealtimeNotifier? notifier = null,
        EventSchemaValidator? validator = null, Func<string, BmEvent?>? eventLookup = null,
        IEventMetrics? metrics = null)
    {
        _logger = logger;
        _notifier = notifier;
        _validator = validator;
        _eventLookup = eventLookup;
        _metrics = metrics;
    }

    /// <summary>
    /// Register an event handler.
    /// </summary>
    public void RegisterHandler(IEventHandler handler)
    {
        _handlers.Add(handler);
    }

    public async Task PublishAsync(string eventName, Dictionary<string, object?> payload, CancellationToken ct = default)
    {
        var domainEvent = new DomainEvent
        {
            EventName = eventName,
            EntityName = ExtractEntityName(eventName),
            EntityId = ExtractEntityId(payload),
            Payload = payload,
            Timestamp = DateTime.UtcNow
        };
        await PublishAsync(domainEvent, ct);
    }

    public async Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
    {
        _logger.LogDebug("Publishing event: {EventName} (EventId: {EventId}, CausationId: {CausationId})",
            @event.EventName, @event.EventId, @event.CausationId);

        // Schema validation (advisory — log warnings but don't block dispatch)
        if (_validator != null && _eventLookup != null)
        {
            var eventSchema = _eventLookup(@event.EventName);
            var validationResult = _validator.Validate(eventSchema, @event.Payload);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Event '{EventName}' payload failed schema validation: {Errors}",
                    @event.EventName, string.Join("; ", validationResult.Errors));
            }
        }

        // Record published metric
        _metrics?.RecordEventPublished(@event.EventName, @event.EntityName, @event.TenantId?.ToString());

        // Notify all registered handlers, threading CausationId for child events
        foreach (var handler in _handlers)
        {
            try
            {
                if (await handler.CanHandleAsync(@event.EventName))
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        await handler.HandleAsync(@event, ct);
                        sw.Stop();
                        _metrics?.RecordEventHandled(@event.EventName, handler.GetType().Name, "success", sw.Elapsed.TotalMilliseconds);
                    }
                    catch
                    {
                        sw.Stop();
                        _metrics?.RecordEventHandled(@event.EventName, handler.GetType().Name, "error", sw.Elapsed.TotalMilliseconds);
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in event handler for {EventName}", @event.EventName);
                // Don't throw - event handlers should not block the main operation
            }
        }

        _logger.LogInformation("Event published: {EventName} (EventId: {EventId}, EntityId: {EntityId})",
            @event.EventName, @event.EventId, @event.EntityId);
    }

    public async Task PublishCreatedAsync(string entityName, Dictionary<string, object?> data, CancellationToken ct = default)
    {
        await PublishAsync($"{entityName}Created", data, ct);

        if (_notifier != null)
        {
            try
            {
                var entityId = ExtractEntityId(data)?.ToString() ?? "";
                await _notifier.NotifyEntityCreatedAsync(entityName, entityId, null, null, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send real-time notification for {EntityName} Created", entityName);
            }
        }
    }

    public async Task PublishUpdatedAsync(string entityName, Dictionary<string, object?> data, 
        Dictionary<string, object?>? changedFields = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>(data);
        if (changedFields != null)
        {
            payload["_changedFields"] = changedFields.Keys.ToList();
        }
        await PublishAsync($"{entityName}Updated", payload, ct);

        if (_notifier != null)
        {
            try
            {
                var entityId = ExtractEntityId(data)?.ToString() ?? "";
                await _notifier.NotifyEntityUpdatedAsync(entityName, entityId, null, null, null, changedFields);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send real-time notification for {EntityName} Updated", entityName);
            }
        }
    }

    public async Task PublishDeletedAsync(string entityName, Guid entityId, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["Id"] = entityId,
            ["DeletedAt"] = DateTime.UtcNow
        };
        await PublishAsync($"{entityName}Deleted", payload, ct);

        if (_notifier != null)
        {
            try
            {
                await _notifier.NotifyEntityDeletedAsync(entityName, entityId.ToString(), null, null, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send real-time notification for {EntityName} Deleted", entityName);
            }
        }
    }

    private static string ExtractEntityName(string eventName)
    {
        // "OrderCreated" -> "Order"
        if (eventName.EndsWith("Created", StringComparison.OrdinalIgnoreCase))
            return eventName[..^7];
        if (eventName.EndsWith("Updated", StringComparison.OrdinalIgnoreCase))
            return eventName[..^7];
        if (eventName.EndsWith("Deleted", StringComparison.OrdinalIgnoreCase))
            return eventName[..^7];
        return eventName;
    }

    private static Guid? ExtractEntityId(Dictionary<string, object?> payload)
    {
        var idValue = payload.GetIdValue();
        if (idValue != null)
        {
            return idValue switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }
        return null;
    }
}

/// <summary>
/// Interface for event handlers.
/// </summary>
public interface IEventHandler
{
    /// <summary>
    /// Check if this handler can handle the given event.
    /// </summary>
    Task<bool> CanHandleAsync(string eventName);
    
    /// <summary>
    /// Handle the event.
    /// </summary>
    Task HandleAsync(DomainEvent @event, CancellationToken ct = default);
}

/// <summary>
/// Audit log event handler - logs all events for auditing.
/// </summary>
public class AuditLogEventHandler : IEventHandler
{
    private readonly ILogger<AuditLogEventHandler> _logger;
    private readonly IAuditLogStore? _store;

    public AuditLogEventHandler(ILogger<AuditLogEventHandler> logger, IAuditLogStore? store = null)
    {
        _logger = logger;
        _store = store;
    }

    public Task<bool> CanHandleAsync(string eventName) => Task.FromResult(true); // Handle all events

    public async Task HandleAsync(DomainEvent @event, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[AUDIT] {EventName} | Entity: {Entity} | Id: {EntityId} | Time: {Timestamp}",
            @event.EventName,
            @event.EntityName,
            @event.EntityId,
            @event.Timestamp);

        if (_store != null)
        {
            await _store.StoreAsync(@event, ct);
        }
    }
}
