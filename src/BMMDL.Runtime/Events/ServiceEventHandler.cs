namespace BMMDL.Runtime.Events;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Service;
using BMMDL.Runtime.Expressions;
using BMMDL.Runtime.Rules;
using BMMDL.Runtime.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// Event handler that routes domain events to service-level event handlers.
/// When a service defines an "on EventName { ... }" block, this handler
/// executes those statements when the matching event is published.
/// Delegates statement execution to <see cref="StatementExecutor"/> with resilient policy.
/// </summary>
public class ServiceEventHandler : IEventHandler
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly IEventPublisher? _eventPublisher;
    private readonly ILogger<ServiceEventHandler> _logger;

    // Access cache through manager asynchronously
    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public ServiceEventHandler(
        MetaModelCacheManager cacheManager,
        ILogger<ServiceEventHandler> logger,
        IEventPublisher? eventPublisher = null)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// Check if any service has a handler for the given event.
    /// </summary>
    public async Task<bool> CanHandleAsync(string eventName)
    {
        var cache = await GetCacheAsync();
        return cache.Services
            .Any(s => s.EventHandlers.Any(h =>
                h.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Execute all service handlers for the given event.
    /// </summary>
    public async Task HandleAsync(DomainEvent @event, CancellationToken ct = default)
    {
        var handlers = await GetHandlersForEventAsync(@event.EventName);

        if (handlers.Count == 0)
        {
            _logger.LogDebug("No service handlers found for event {EventName}", @event.EventName);
            return;
        }

        _logger.LogInformation("Executing {Count} service handler(s) for event {EventName}",
            handlers.Count, @event.EventName);

        foreach (var (service, handler) in handlers)
        {
            try
            {
                await ExecuteHandlerAsync(service, handler, @event, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error executing handler in service {Service} for event {EventName}",
                    service.Name, @event.EventName);
                // Continue with other handlers - don't let one failure stop others
            }
        }
    }

    /// <summary>
    /// Get all handlers for a specific event name.
    /// </summary>
    private async Task<List<(BmService Service, BmEventHandler Handler)>> GetHandlersForEventAsync(string eventName)
    {
        var cache = await GetCacheAsync();
        var result = new List<(BmService, BmEventHandler)>();

        foreach (var service in cache.Services)
        {
            foreach (var handler in service.EventHandlers)
            {
                if (handler.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add((service, handler));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Execute a single event handler using the shared StatementExecutor.
    /// </summary>
    private async Task ExecuteHandlerAsync(
        BmService service,
        BmEventHandler handler,
        DomainEvent @event,
        CancellationToken ct)
    {
        _logger.LogDebug("Executing handler in service {Service} for event {EventName}",
            service.Name, @event.EventName);

        // Build evaluation context with event data
        var context = new EvaluationContext
        {
            TenantId = @event.TenantId,
            Parameters = new Dictionary<string, object?>
            {
                ["$event"] = @event.Payload,
                ["$eventName"] = @event.EventName,
                ["$entityName"] = @event.EntityName,
                ["$entityId"] = @event.EntityId,
                ["$timestamp"] = @event.Timestamp,
                ["$correlationId"] = @event.CorrelationId
            }
        };

        // Merge event payload into parameters for easier access
        foreach (var kvp in @event.Payload)
        {
            context.Parameters[kvp.Key] = kvp.Value;
        }

        // Create a StatementExecutor with resilient policy for event handlers
        var cache = await GetCacheAsync();
        var evaluator = new RuntimeExpressionEvaluator();
        var callTargetResolver = new CallTargetResolver(cache);
        var executor = new StatementExecutor(evaluator, cache, _logger, callTargetResolver, ResilientPolicy.Instance, eventPublisher: _eventPublisher);

        // Execute all handler statements
        foreach (var stmt in handler.Statements)
        {
            ct.ThrowIfCancellationRequested();
            var result = await executor.ExecuteStatementAsync(stmt, context);

            // Event handlers stop on reject or return (but don't propagate errors)
            if (result.Rejected || result.ShouldReturn) break;
        }

        _logger.LogDebug("Completed handler in service {Service} for event {EventName}",
            service.Name, @event.EventName);
    }
}
