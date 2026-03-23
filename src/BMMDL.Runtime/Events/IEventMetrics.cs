namespace BMMDL.Runtime.Events;

/// <summary>
/// Abstraction for event-related metrics recording.
/// Implemented by BmmdlMetrics in Runtime.Api layer.
/// </summary>
public interface IEventMetrics
{
    void RecordEventPublished(string eventName, string entityName, string? tenant);
    void RecordEventHandled(string eventName, string handler, string status, double durationMs);
    void RecordDeadLetter(string eventName);
}
