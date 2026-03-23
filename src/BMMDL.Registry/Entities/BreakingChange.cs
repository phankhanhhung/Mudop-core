namespace BMMDL.Registry.Entities;

/// <summary>
/// Records individual breaking changes that require approval.
/// </summary>
public class BreakingChange
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Parent ObjectVersion containing this breaking change.
    /// </summary>
    public Guid ObjectVersionId { get; set; }
    public ObjectVersion? ObjectVersion { get; set; }
    
    /// <summary>
    /// Type of change: FieldRemoved, TypeNarrowed, EnumValueRemoved, EntityRemoved, etc.
    /// </summary>
    public string ChangeType { get; set; } = "";
    
    /// <summary>
    /// Category: Major (always), since only Major changes are breaking.
    /// </summary>
    public string Category { get; set; } = "Major";
    
    /// <summary>
    /// Target object affected (e.g., field name, enum value).
    /// </summary>
    public string? TargetName { get; set; }
    
    /// <summary>
    /// Previous value/definition before the change.
    /// </summary>
    public string? OldValue { get; set; }
    
    /// <summary>
    /// New value/definition after the change.
    /// </summary>
    public string? NewValue { get; set; }
    
    /// <summary>
    /// Human-readable description of the impact.
    /// </summary>
    public string Description { get; set; } = "";
    
    /// <summary>
    /// Potential impact on existing data (data loss risk, etc.)
    /// </summary>
    public string? ImpactAnalysis { get; set; }
    
    /// <summary>
    /// Suggested migration action.
    /// </summary>
    public string? SuggestedAction { get; set; }
    
    /// <summary>
    /// Status of this specific breaking change.
    /// </summary>
    public BreakingChangeStatus Status { get; set; } = BreakingChangeStatus.Pending;
    
    /// <summary>
    /// Reviewer comments/notes.
    /// </summary>
    public string? ReviewerNotes { get; set; }
    
    /// <summary>
    /// Who reviewed/approved this change.
    /// </summary>
    public string? ReviewedBy { get; set; }
    
    /// <summary>
    /// When this change was reviewed.
    /// </summary>
    public DateTime? ReviewedAt { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum BreakingChangeStatus
{
    /// <summary>
    /// Pending review.
    /// </summary>
    Pending,
    
    /// <summary>
    /// Acknowledged and approved by reviewer.
    /// </summary>
    Approved,
    
    /// <summary>
    /// Rejected, change will not be applied.
    /// </summary>
    Rejected,
    
    /// <summary>
    /// Requires further discussion/modification.
    /// </summary>
    NeedsWork
}
