namespace BMMDL.SchemaManager;

/// <summary>
/// Information about an applied migration.
/// </summary>
public class MigrationInfo
{
    /// <summary>
    /// Migration name/identifier.
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// When the migration was applied.
    /// </summary>
    public DateTime AppliedAt { get; set; }
    
    /// <summary>
    /// Checksum of the migration script.
    /// </summary>
    public string? Checksum { get; set; }
    
    /// <summary>
    /// The UP script that was executed.
    /// </summary>
    public string? UpScript { get; set; }
    
    /// <summary>
    /// The DOWN script for rollback.
    /// </summary>
    public string? DownScript { get; set; }
}
