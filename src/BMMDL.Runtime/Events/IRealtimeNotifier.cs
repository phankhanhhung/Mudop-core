namespace BMMDL.Runtime.Events;

/// <summary>
/// Abstraction for pushing real-time notifications to connected clients.
/// Implemented by SignalR in Runtime.Api.
/// </summary>
public interface IRealtimeNotifier
{
    Task NotifyEntityCreatedAsync(string entityName, string entityId, string? module, string? tenantId, string? userId);
    Task NotifyEntityUpdatedAsync(string entityName, string entityId, string? module, string? tenantId, string? userId, Dictionary<string, object?>? changedFields);
    Task NotifyEntityDeletedAsync(string entityName, string entityId, string? module, string? tenantId, string? userId);
}
