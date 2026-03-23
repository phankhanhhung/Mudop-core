using BMMDL.Runtime.Events;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Background service to register event handlers after app startup.
/// Handlers are optional — if a plugin that provides a handler is not installed,
/// it is simply not registered.
/// </summary>
public class EventHandlerRegistrationService : IHostedService
{
    private readonly IEventPublisher _eventPublisher;
    private readonly AuditLogEventHandler? _auditHandler;
    private readonly ServiceEventHandler _serviceHandler;

    public EventHandlerRegistrationService(
        IEventPublisher eventPublisher,
        ServiceEventHandler serviceHandler,
        AuditLogEventHandler? auditHandler = null)
    {
        _eventPublisher = eventPublisher;
        _serviceHandler = serviceHandler;
        _auditHandler = auditHandler;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_eventPublisher is EventPublisher publisher)
        {
            if (_auditHandler is not null)
                publisher.RegisterHandler(_auditHandler);
            publisher.RegisterHandler(_serviceHandler);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
