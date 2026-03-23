namespace BMMDL.Runtime.Events;

using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Outbox batch processor logic. Polls the outbox store for pending entries and dispatches
/// them via the EventPublisher. Designed to be driven by a hosted service in the API layer.
/// </summary>
public class OutboxProcessor
{
    private readonly IOutboxStore _outboxStore;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly IEventMetrics? _metrics;
    private readonly IBrokerAdapter? _brokerAdapter;
    private readonly int _batchSize;

    public OutboxProcessor(
        IOutboxStore outboxStore,
        IEventPublisher eventPublisher,
        ILogger<OutboxProcessor> logger,
        int batchSize = 50,
        IEventMetrics? metrics = null,
        IBrokerAdapter? brokerAdapter = null)
    {
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batchSize = batchSize;
        _metrics = metrics;
        _brokerAdapter = brokerAdapter;
    }

    /// <summary>
    /// Process a batch of pending outbox entries.
    /// </summary>
    public async Task ProcessBatchAsync(CancellationToken ct = default)
    {
        var entries = await _outboxStore.GetPendingAsync(_batchSize, ct);

        if (entries.Count == 0)
            return;

        _logger.LogDebug("OutboxProcessor: processing {Count} pending entries", entries.Count);

        foreach (var entry in entries)
        {
            try
            {
                if (entry.IsIntegration && _brokerAdapter != null)
                {
                    // Integration event → route to external broker adapter
                    var domainEvent = ReconstructDomainEvent(entry);
                    await _brokerAdapter.PublishAsync(domainEvent, ct);
                    _logger.LogDebug("Outbox entry {Id} published to broker '{Broker}': {EventName}",
                        entry.Id, _brokerAdapter.Name, entry.EventName);
                }
                else
                {
                    // Domain event → route to in-memory event publisher
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(entry.Payload)
                        ?? new Dictionary<string, object?>();
                    await _eventPublisher.PublishAsync(entry.EventName, payload, ct);
                    _logger.LogDebug("Outbox entry {Id} delivered via publisher: {EventName}", entry.Id, entry.EventName);
                }

                await _outboxStore.MarkDeliveredAsync(entry.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox entry {Id} failed: {EventName} (retry {Retry}/{Max})",
                    entry.Id, entry.EventName, entry.RetryCount + 1, entry.MaxRetries);

                try
                {
                    await _outboxStore.MarkFailedAsync(entry.Id, ex.Message, ct);
                    // Record dead letter metric when max retries exceeded
                    if (entry.RetryCount + 1 >= entry.MaxRetries)
                    {
                        _metrics?.RecordDeadLetter(entry.EventName);
                    }
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "Failed to mark outbox entry {Id} as failed", entry.Id);
                }
            }
        }
    }

    /// <summary>
    /// Reconstruct a DomainEvent from an OutboxEntry for broker publishing.
    /// Parses tracing fields (correlationId, causationId, etc.) from the context JSONB.
    /// </summary>
    private static DomainEvent ReconstructDomainEvent(OutboxEntry entry)
    {
        string? correlationId = null;
        string? causationId = null;
        Guid? userId = null;
        string? sourceModule = null;
        Guid eventId = Guid.NewGuid();

        if (entry.Context != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(entry.Context);
                var root = doc.RootElement;

                if (root.TryGetProperty("eventId", out var eid) && eid.ValueKind == JsonValueKind.String
                    && Guid.TryParse(eid.GetString(), out var parsedEventId))
                    eventId = parsedEventId;

                if (root.TryGetProperty("correlationId", out var cid) && cid.ValueKind == JsonValueKind.String)
                    correlationId = cid.GetString();

                if (root.TryGetProperty("causationId", out var caid) && caid.ValueKind == JsonValueKind.String)
                    causationId = caid.GetString();

                if (root.TryGetProperty("userId", out var uid) && uid.ValueKind == JsonValueKind.String
                    && Guid.TryParse(uid.GetString(), out var parsedUserId))
                    userId = parsedUserId;

                if (root.TryGetProperty("sourceModule", out var sm) && sm.ValueKind == JsonValueKind.String)
                    sourceModule = sm.GetString();
            }
            catch (JsonException)
            {
                // Malformed JSON context is non-fatal — defaults (new correlation/causation IDs) are used.
                // This can occur if an older event was persisted with an incompatible context format.
            }
        }

        return new DomainEvent
        {
            EventId = eventId,
            EventName = entry.EventName,
            EntityName = entry.EntityName,
            EntityId = entry.EntityId,
            TenantId = entry.TenantId,
            Payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(entry.Payload)
                ?? new Dictionary<string, object?>(),
            SchemaVersion = entry.SchemaVersion,
            CorrelationId = correlationId,
            CausationId = causationId,
            UserId = userId,
            SourceModule = sourceModule
        };
    }
}
