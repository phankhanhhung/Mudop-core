namespace BMMDL.Runtime.Plugins.Staging;

/// <summary>
/// Validation status for a staged plugin.
/// </summary>
public enum StagingValidationStatus
{
    /// <summary>Validation has not been run yet.</summary>
    Pending,
    /// <summary>All validation checks passed.</summary>
    Valid,
    /// <summary>One or more validation checks failed.</summary>
    Invalid,
    /// <summary>Plugin has been approved for installation.</summary>
    Approved,
    /// <summary>Plugin has been rejected by admin.</summary>
    Rejected
}

/// <summary>
/// Persisted record for a staged (uploaded but not yet installed) plugin.
/// Stored in <c>core.plugin_staging</c>.
/// </summary>
public sealed record PluginStagingRecord
{
    /// <summary>Unique staging ID.</summary>
    public required int Id { get; init; }

    /// <summary>Plugin name from manifest.</summary>
    public required string Name { get; init; }

    /// <summary>Plugin version from manifest.</summary>
    public required string Version { get; init; }

    /// <summary>Plugin description from manifest.</summary>
    public string? Description { get; init; }

    /// <summary>Plugin author from manifest.</summary>
    public string? Author { get; init; }

    /// <summary>SHA-256 hash of the original uploaded zip file.</summary>
    public required string FileHash { get; init; }

    /// <summary>Size of the original uploaded zip file in bytes.</summary>
    public required long FileSize { get; init; }

    /// <summary>Original uploaded file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Path to the extracted staging directory.</summary>
    public required string StagingPath { get; init; }

    /// <summary>Current validation status.</summary>
    public required StagingValidationStatus ValidationStatus { get; init; }

    /// <summary>When the plugin was uploaded.</summary>
    public required DateTimeOffset UploadedAt { get; init; }

    /// <summary>When the plugin was approved (null if not yet approved).</summary>
    public DateTimeOffset? ApprovedAt { get; init; }

    /// <summary>Validation results from the validation pipeline.</summary>
    public IReadOnlyList<ValidationCheckResult> ValidationResults { get; init; } = [];
}
