using BMMDL.Runtime.Events;
using BMMDL.Runtime.Plugins;

namespace BMMDL.Runtime.Api.Services;

/// <summary>
/// Background hosted service that drives the OutboxProcessor on a polling interval.
/// Plugin-aware: if OutboxProcessor is not available (plugin not loaded) or EventOutbox
/// plugin is not enabled, the service stays dormant.
/// </summary>
public class OutboxProcessorService : BackgroundService
{
    private readonly OutboxProcessor? _processor;
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _dormantCheckInterval = TimeSpan.FromSeconds(60);

    private const string RequiredPlugin = "EventOutbox";

    public OutboxProcessorService(
        IPluginManager pluginManager,
        ILogger<OutboxProcessorService> logger,
        OutboxProcessor? processor = null,
        TimeSpan? pollInterval = null)
    {
        _processor = processor;
        _pluginManager = pluginManager;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_processor is null)
        {
            _logger.LogInformation("OutboxProcessorService: OutboxProcessor not available (EventOutbox plugin not loaded). Service dormant.");
            return;
        }

        _logger.LogInformation("OutboxProcessorService started (poll interval: {Interval}s)",
            _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await IsPluginEnabledAsync(stoppingToken))
            {
                try
                {
                    await Task.Delay(_dormantCheckInterval, stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                await _processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxProcessorService encountered an error during batch processing");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("OutboxProcessorService stopped");
    }

    private async Task<bool> IsPluginEnabledAsync(CancellationToken ct)
    {
        try
        {
            var state = await _pluginManager.GetPluginStateAsync(RequiredPlugin, ct);
            return state?.Status == PluginStatus.Enabled;
        }
        catch
        {
            return false;
        }
    }
}
