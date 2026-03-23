using BMMDL.Runtime.Events;
using Microsoft.AspNetCore.SignalR;

namespace BMMDL.Runtime.Api.Hubs;

/// <summary>
/// Implements IRealtimeNotifier by forwarding events to SignalR clients.
/// Sends to both tenant-specific and entity-specific groups.
/// </summary>
public class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationHub, INotificationClient> _hubContext;
    private readonly ILogger<SignalRNotifier> _logger;

    public SignalRNotifier(
        IHubContext<NotificationHub, INotificationClient> hubContext,
        ILogger<SignalRNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyEntityCreatedAsync(string entityName, string entityId, string? module, string? tenantId, string? userId)
    {
        var notification = new EntityChangeNotification(
            entityName, entityId, module ?? "", userId, tenantId, DateTime.UtcNow);

        var tasks = new List<Task>
        {
            _hubContext.Clients.Group($"entity:{entityName}").EntityCreated(notification)
        };

        if (tenantId != null)
            tasks.Add(_hubContext.Clients.Group($"tenant:{tenantId}").EntityCreated(notification));

        await Task.WhenAll(tasks);
        _logger.LogDebug("SignalR: EntityCreated {EntityName}/{EntityId}", entityName, entityId);
    }

    public async Task NotifyEntityUpdatedAsync(string entityName, string entityId, string? module, string? tenantId, string? userId, Dictionary<string, object?>? changedFields)
    {
        var notification = new EntityChangeNotification(
            entityName, entityId, module ?? "", userId, tenantId, DateTime.UtcNow, changedFields);

        var tasks = new List<Task>
        {
            _hubContext.Clients.Group($"entity:{entityName}").EntityUpdated(notification)
        };

        if (tenantId != null)
            tasks.Add(_hubContext.Clients.Group($"tenant:{tenantId}").EntityUpdated(notification));

        await Task.WhenAll(tasks);
        _logger.LogDebug("SignalR: EntityUpdated {EntityName}/{EntityId}", entityName, entityId);
    }

    public async Task NotifyEntityDeletedAsync(string entityName, string entityId, string? module, string? tenantId, string? userId)
    {
        var notification = new EntityChangeNotification(
            entityName, entityId, module ?? "", userId, tenantId, DateTime.UtcNow);

        var tasks = new List<Task>
        {
            _hubContext.Clients.Group($"entity:{entityName}").EntityDeleted(notification)
        };

        if (tenantId != null)
            tasks.Add(_hubContext.Clients.Group($"tenant:{tenantId}").EntityDeleted(notification));

        await Task.WhenAll(tasks);
        _logger.LogDebug("SignalR: EntityDeleted {EntityName}/{EntityId}", entityName, entityId);
    }
}
