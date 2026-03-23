using System;
using System.Collections.Generic;

namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Represents a database migration.
/// </summary>
public class Migration
{
    public string Name { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string UpScript { get; set; } = "";
    public string DownScript { get; set; } = "";
    public string Checksum { get; set; } = "";
}

/// <summary>
/// Result of migration execution.
/// </summary>
public class MigrationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string Script { get; set; } = "";
    public bool DryRun { get; set; }
}

/// <summary>
/// Represents a single migration step.
/// </summary>
public class MigrationStep
{
    public int Order { get; set; }
    public string Description { get; set; } = "";
    public string Sql { get; set; } = "";
}
