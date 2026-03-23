using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime.Events;

/// <summary>
/// No-op broker adapter used when no real message broker is configured.
/// Logs the event at debug level and discards it. Always reports healthy.
/// </summary>
public class NullBrokerAdapter : IBrokerAdapter
{
    private readonly ILogger<NullBrokerAdapter> _logger;

    public NullBrokerAdapter(ILogger<NullBrokerAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "null";

    public Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
    {
        _logger.LogDebug("NullBrokerAdapter: discarding event {EventName} (no broker configured)", @event.EventName);
        return Task.CompletedTask;
    }

    public Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}
