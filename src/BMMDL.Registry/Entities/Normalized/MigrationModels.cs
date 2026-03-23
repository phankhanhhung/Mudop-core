namespace BMMDL.Registry.Entities.Normalized;

// ============================================================
// MIGRATION DEFINITION MODELS (3 tables)
// ============================================================

/// <summary>
/// Migration definition record — persists BmMigrationDef to registry.
/// </summary>
public class MigrationDefRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? NamespaceId { get; set; }
    public Guid? ModuleId { get; set; }
    public string Name { get; set; } = "";
    public string QualifiedName { get; set; } = "";
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public bool Breaking { get; set; }
    public string? DependenciesJson { get; set; } // JSON array of dependency names
    public Guid? SourceFileId { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant? Tenant { get; set; }
    public Module? Module { get; set; }
    public Namespace? Namespace { get; set; }
    public SourceFile? SourceFile { get; set; }
    public ICollection<MigrationStepRecord> Steps { get; } = new List<MigrationStepRecord>();
    public ICollection<NormalizedAnnotation> Annotations { get; } = new List<NormalizedAnnotation>();
}

/// <summary>
/// Migration step record — represents a single up/down step in a migration.
/// Steps are stored as JSON to preserve their polymorphic structure
/// (ALTER ENTITY, ADD ENTITY, DROP ENTITY, TRANSFORM).
/// </summary>
public class MigrationStepRecord
{
    public Guid Id { get; set; }
    public Guid MigrationDefId { get; set; }
    public string Direction { get; set; } = ""; // "up" or "down"
    public string StepType { get; set; } = ""; // "alter_entity", "add_entity", "drop_entity", "transform"
    public string StepJson { get; set; } = ""; // Full step serialized as JSON
    public int Position { get; set; }

    // Navigation
    public MigrationDefRecord MigrationDef { get; set; } = null!;
}
