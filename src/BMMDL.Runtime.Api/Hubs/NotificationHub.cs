using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BMMDL.Runtime.Api.Hubs;

public interface INotificationClient
{
    Task EntityCreated(EntityChangeNotification notification);
    Task EntityUpdated(EntityChangeNotification notification);
    Task EntityDeleted(EntityChangeNotification notification);

    // Collaboration
    Task RecordLocked(RecordLockNotification notification);
    Task RecordUnlocked(RecordUnlockNotification notification);
    Task NewComment(CommentNotification notification);
    Task ChangeRequestUpdated(ChangeRequestNotification notification);
}

public record EntityChangeNotification(
    string EntityName,
    string EntityId,
    string Module,
    string? UserId,
    string? TenantId,
    DateTime Timestamp,
    Dictionary<string, object?>? ChangedFields = null
);

public record RecordLockNotification(string RecordKey, string UserId, string DisplayName, DateTime StartedAt);
public record RecordUnlockNotification(string RecordKey, string UserId);
public record CommentNotification(string RecordKey, string CommentId, string AuthorId, string AuthorName, string Content, string[] Mentions, DateTime CreatedAt);
public record ChangeRequestNotification(string RecordKey, string RequestId, string Status, string ProposedByName);

[Authorize]
public class NotificationHub : Hub<INotificationClient>
{
    // In-memory record lock tracking: recordKey → (connectionId, userId, displayName, startedAt)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RecordLockInfo> _recordLocks = new();

    public record RecordLockInfo(string ConnectionId, string UserId, string DisplayName, DateTime StartedAt);

    public async Task JoinTenantGroup(string tenantId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
    }

    public async Task LeaveTenantGroup(string tenantId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
    }

    public async Task JoinEntityGroup(string entityName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"entity:{entityName}");
    }

    public async Task LeaveEntityGroup(string entityName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"entity:{entityName}");
    }

    public async Task JoinRecordGroup(string recordKey)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"record:{recordKey}");
    }

    public async Task LeaveRecordGroup(string recordKey)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"record:{recordKey}");
    }

    public async Task StartEditing(string recordKey, string displayName)
    {
        var userId = Context.UserIdentifier;
        if (userId is null)
            return; // [Authorize] ensures authenticated users; guard against edge cases

        // Atomic lock acquisition: GetOrAdd ensures only one thread wins the race
        var lockInfo = new RecordLockInfo(Context.ConnectionId, userId, displayName, DateTime.UtcNow);
        var actualLock = _recordLocks.GetOrAdd(recordKey, lockInfo);

        if (actualLock == lockInfo)
        {
            // We acquired the lock — broadcast to group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"record:{recordKey}");
            await Clients.Group($"record:{recordKey}").RecordLocked(
                new RecordLockNotification(recordKey, userId, displayName, lockInfo.StartedAt));
        }
        else if (actualLock.ConnectionId == Context.ConnectionId)
        {
            // We already hold the lock — update it atomically
            _recordLocks.TryUpdate(recordKey, lockInfo, actualLock);
            await Clients.Group($"record:{recordKey}").RecordLocked(
                new RecordLockNotification(recordKey, userId, displayName, lockInfo.StartedAt));
        }
        // else: another user holds the lock — do nothing (caller can check lock status)
    }

    public async Task StopEditing(string recordKey)
    {
        var userId = Context.UserIdentifier ?? Context.ConnectionId;
        // Atomic compare-and-remove: TryGetValue to snapshot, then remove only if
        // the entry still matches via ICollection<KVP>.Remove (atomic in ConcurrentDictionary).
        // If another thread changed the value between TryGetValue and Remove, Remove returns
        // false and we correctly do nothing (we no longer hold the lock).
        if (_recordLocks.TryGetValue(recordKey, out var lockInfo) &&
            lockInfo.ConnectionId == Context.ConnectionId &&
            ((ICollection<KeyValuePair<string, RecordLockInfo>>)_recordLocks)
                .Remove(new KeyValuePair<string, RecordLockInfo>(recordKey, lockInfo)))
        {
            await Clients.Group($"record:{recordKey}").RecordUnlocked(
                new RecordUnlockNotification(recordKey, userId));
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Release all record locks held by this connection.
        // Snapshot key-value pairs (not just keys) so we can do atomic compare-and-remove.
        var myLocks = _recordLocks
            .Where(kvp => kvp.Value.ConnectionId == Context.ConnectionId)
            .ToList();

        var userId = Context.UserIdentifier ?? Context.ConnectionId;
        var lockCollection = (ICollection<KeyValuePair<string, RecordLockInfo>>)_recordLocks;
        foreach (var kvp in myLocks)
        {
            // Atomic compare-and-remove: only removes if the entry still has the exact
            // same value. If another thread replaced it (e.g., new user took the lock),
            // Remove returns false and we correctly skip the notification.
            if (lockCollection.Remove(kvp))
            {
                await Clients.Group($"record:{kvp.Key}").RecordUnlocked(
                    new RecordUnlockNotification(kvp.Key, userId));
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Returns the current lock holder for a record, or null if unlocked.
    /// Called by CollaborationController to serve the GET lock endpoint.
    /// </summary>
    public static RecordLockInfo? GetLock(string recordKey)
        => _recordLocks.TryGetValue(recordKey, out var info) ? info : null;
}
