namespace BMMDL.Registry.Api.Models;

/// <summary>
/// Request to clear database (drop schemas, truncate registry tables).
/// </summary>
public record ClearDatabaseRequest
{
    /// <summary>Truncate registry tables (modules, entities, etc.)</summary>
    public bool ClearRegistry { get; init; } = true;
    
    /// <summary>Drop business schemas</summary>
    public bool DropSchemas { get; init; } = true;
    
    /// <summary>Specific schemas to drop. If null or empty, drops all known business schemas.</summary>
    public List<string>? Schemas { get; init; }
}

/// <summary>
/// Response from clear database operation.
/// </summary>
public record ClearDatabaseResponse
{
    public bool Success { get; init; }
    public int SchemasDropped { get; init; }
    public int TablesCleared { get; init; }
    public List<string> Messages { get; init; } = new();
    public string? Error { get; init; }
}

/// <summary>
/// Request to compile BMMDL source and install into system.
/// </summary>
public record CompileRequest
{
    /// <summary>BMMDL source code</summary>
    public string BmmdlSource { get; init; } = "";
    
    /// <summary>Module name (for error reporting and identification)</summary>
    public string ModuleName { get; init; } = "";
    
    /// <summary>Tenant ID for module ownership (default: system tenant)</summary>
    public Guid? TenantId { get; init; }
    
    /// <summary>Publish compiled model to registry</summary>
    public bool Publish { get; init; } = true;
    
    /// <summary>Initialize business tables after publishing</summary>
    public bool InitSchema { get; init; } = false;
    
    /// <summary>Force schema recreation (drops existing tables)</summary>
    public bool Force { get; init; } = false;
}

/// <summary>
/// Response from compile operation.
/// </summary>
public record CompileResponse
{
    public bool Success { get; init; }
    public int EntityCount { get; init; }
    public int ServiceCount { get; init; }
    public int TypeCount { get; init; }
    public int EnumCount { get; init; }
    public TimeSpan CompilationTime { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public string? SchemaResult { get; init; }

    /// <summary>Version info if versioning was processed.</summary>
    public VersionInfoDto? VersionInfo { get; init; }

    /// <summary>Structured compilation diagnostics with error codes, locations, and severity.</summary>
    public List<CompileDiagnosticDto> Diagnostics { get; init; } = new();
}

/// <summary>
/// Structured compilation diagnostic with code, message, severity, and source location.
/// </summary>
public record CompileDiagnosticDto
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public string Severity { get; init; } = "";
    public string? SourceFile { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string? Pass { get; init; }
}

/// <summary>
/// Version information from versioning system.
/// </summary>
public record VersionInfoDto
{
    public string Version { get; init; } = "";
    public string ChangeCategory { get; init; } = "";
    public int TotalChanges { get; init; }
    public bool HasBreakingChanges { get; init; }
    public bool RequiresApproval { get; init; }
    public string? MigrationSql { get; init; }
}

/// <summary>
/// Response from bootstrap platform operation.
/// </summary>
public record BootstrapResponse
{
    public bool Success { get; init; }
    public List<string> Messages { get; init; } = new();
    public string? Error { get; init; }
    public int EntityCount { get; init; }
}

/// <summary>
/// Module info with schema initialization status.
/// </summary>
public record ModuleStatusDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string? Author { get; init; }
    public int EntityCount { get; init; }
    public int ServiceCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PublishedAt { get; init; }
    public bool SchemaInitialized { get; init; }
    public int TableCount { get; init; }
    public string? SchemaName { get; init; }
    public List<ModuleDependencyDto> Dependencies { get; init; } = new();
}

/// <summary>
/// Module dependency info for dependency graph visualization.
/// </summary>
public record ModuleDependencyDto
{
    public string DependsOnName { get; init; } = "";
    public string VersionRange { get; init; } = "";
    public Guid? ResolvedId { get; init; }
    public bool IsResolved { get; init; }
}

/// <summary>
/// Request to preview DDL for a module without executing it.
/// </summary>
public record DdlPreviewRequest
{
    public string BmmdlSource { get; init; } = "";
    public string ModuleName { get; init; } = "";
}

/// <summary>
/// Response containing generated DDL for preview.
/// </summary>
public record DdlPreviewResponse
{
    public bool Success { get; init; }
    public string Ddl { get; init; } = "";
    public int TableCount { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

/// <summary>
/// Response from module uninstall operation.
/// </summary>
public record UninstallModuleResponse
{
    public bool Success { get; init; }
    public List<string> Messages { get; init; } = new();
    public string? Error { get; init; }
    /// <summary>Modules that depend on this one (if uninstall is blocked).</summary>
    public List<string>? DependentModules { get; init; }
}

/// <summary>
/// Dependency graph response with nodes and edges for visualization.
/// </summary>
public record DependencyGraphResponse
{
    public List<DependencyGraphNode> Nodes { get; init; } = new();
    public List<DependencyGraphEdge> Edges { get; init; } = new();
}

/// <summary>
/// A node in the dependency graph representing an installed module.
/// </summary>
public record DependencyGraphNode
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Status { get; init; } = "";
}

/// <summary>
/// An edge in the dependency graph representing a dependency relationship.
/// </summary>
public record DependencyGraphEdge
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string VersionRange { get; init; } = "";
}
