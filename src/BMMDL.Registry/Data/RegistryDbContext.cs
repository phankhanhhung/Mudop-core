using Microsoft.EntityFrameworkCore;
using BMMDL.Registry.Entities;
using BMMDL.Registry.Entities.Normalized;

namespace BMMDL.Registry.Data;

/// <summary>
/// Unified DbContext for Meta Model Registry - Single source of truth for all MetaModel data.
/// Includes both module lifecycle management and normalized MetaModel storage.
/// </summary>
public class RegistryDbContext : DbContext
{
    public RegistryDbContext(DbContextOptions<RegistryDbContext> options) 
        : base(options)
    {
    }

    // ============================================================
    // MODULE LIFECYCLE (existing)
    // ============================================================
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<ModuleDependency> ModuleDependencies => Set<ModuleDependency>();
    public DbSet<ModuleDeprecation> ModuleDeprecations => Set<ModuleDeprecation>();
    public DbSet<ModuleInstallation> ModuleInstallations => Set<ModuleInstallation>();
    public DbSet<Migration> Migrations => Set<Migration>();
    
    // Legacy blob storage DbSets REMOVED (ModelPackage, ModelElement, etc.)

    // ============================================================
    // NORMALIZED METAMODEL (new)
    // ============================================================
    
    // Core
    public DbSet<Namespace> Namespaces => Set<Namespace>();
    public DbSet<SourceFile> SourceFiles => Set<SourceFile>();
    public DbSet<NormalizedAnnotation> NormalizedAnnotations => Set<NormalizedAnnotation>();
    
    // Entities
    public DbSet<EntityRecord> Entities => Set<EntityRecord>();
    public DbSet<EntityField> EntityFields => Set<EntityField>();
    public DbSet<EntityAssociation> EntityAssociations => Set<EntityAssociation>();
    public DbSet<EntityAspectRef> EntityAspectRefs => Set<EntityAspectRef>();
    public DbSet<EntityIndex> EntityIndexes => Set<EntityIndex>();
    public DbSet<EntityIndexField> EntityIndexFields => Set<EntityIndexField>();
    public DbSet<EntityConstraint> EntityConstraints => Set<EntityConstraint>();
    public DbSet<EntityConstraintField> EntityConstraintFields => Set<EntityConstraintField>();
    
    // Bound Operations (actions/functions on entities)
    public DbSet<EntityBoundOperation> EntityBoundOperations => Set<EntityBoundOperation>();
    public DbSet<BoundOperationParameter> BoundOperationParameters => Set<BoundOperationParameter>();
    public DbSet<BoundOperationEmit> BoundOperationEmits => Set<BoundOperationEmit>();
    
    // Types & Enums
    public DbSet<TypeRecord> Types => Set<TypeRecord>();
    public DbSet<TypeField> TypeFields => Set<TypeField>();
    public DbSet<EnumRecord> Enums => Set<EnumRecord>();
    public DbSet<EnumValue> EnumValues => Set<EnumValue>();
    
    // Aspects
    public DbSet<AspectRecord> Aspects => Set<AspectRecord>();
    public DbSet<AspectInclude> AspectIncludes => Set<AspectInclude>();
    public DbSet<AspectField> AspectFields => Set<AspectField>();
    
    // Services
    public DbSet<ServiceRecord> Services => Set<ServiceRecord>();
    public DbSet<ServiceExposedEntity> ServiceExposedEntities => Set<ServiceExposedEntity>();
    public DbSet<ServiceOperation> ServiceOperations => Set<ServiceOperation>();
    public DbSet<OperationParameter> OperationParameters => Set<OperationParameter>();
    public DbSet<ServiceOperationEmit> ServiceOperationEmits => Set<ServiceOperationEmit>();
    public DbSet<ServiceEventHandler> ServiceEventHandlers => Set<ServiceEventHandler>();
    
    // Views
    public DbSet<ViewRecord> Views => Set<ViewRecord>();
    public DbSet<ViewParameter> ViewParameters => Set<ViewParameter>();
    
    // Access Controls
    public DbSet<AccessControlRecord> AccessControls => Set<AccessControlRecord>();
    public DbSet<AccessRule> AccessRules => Set<AccessRule>();
    public DbSet<AccessRuleOperation> AccessRuleOperations => Set<AccessRuleOperation>();
    public DbSet<AccessRulePrincipal> AccessRulePrincipals => Set<AccessRulePrincipal>();
    public DbSet<AccessFieldRestriction> AccessFieldRestrictions => Set<AccessFieldRestriction>();
    
    // Rules
    public DbSet<RuleRecord> Rules => Set<RuleRecord>();
    public DbSet<RuleTrigger> RuleTriggers => Set<RuleTrigger>();
    public DbSet<RuleTriggerField> RuleTriggerFields => Set<RuleTriggerField>();
    public DbSet<RuleStatement> RuleStatements => Set<RuleStatement>();
    
    // Sequences & Events
    public DbSet<SequenceRecord> Sequences => Set<SequenceRecord>();
    public DbSet<EventRecord> Events => Set<EventRecord>();
    public DbSet<EventField> EventFields => Set<EventField>();

    // Migration Definitions
    public DbSet<MigrationDefRecord> MigrationDefs => Set<MigrationDefRecord>();
    public DbSet<MigrationStepRecord> MigrationSteps => Set<MigrationStepRecord>();
    
    // Expression AST
    public DbSet<ExpressionNode> ExpressionNodes => Set<ExpressionNode>();
    
    // Statement AST (action/function body nodes)
    public DbSet<StatementNode> StatementNodes => Set<StatementNode>();
    
    // ============================================================
    // VERSIONING (Phase 1c)
    // ============================================================
    public DbSet<ObjectVersion> ObjectVersions => Set<ObjectVersion>();
    public DbSet<BreakingChange> BreakingChanges => Set<BreakingChange>();
    
    // ============================================================
    // DUAL-VERSION SYNC (Phase 2)
    // ============================================================
    public DbSet<UpgradeWindow> UpgradeWindows => Set<UpgradeWindow>();
    public DbSet<UpgradeSyncStatus> UpgradeSyncStatuses => Set<UpgradeSyncStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Set default schema for all registry tables
        modelBuilder.HasDefaultSchema("registry");

        // ============================================================
        // MODULE LIFECYCLE TABLES (existing)
        // ============================================================
        
        ConfigureModuleLifecycleTables(modelBuilder);
        
        // ============================================================
        // NORMALIZED METAMODEL TABLES (new)
        // ============================================================
        
        ConfigureNormalizedCoreTables(modelBuilder);
        ConfigureNormalizedEntityTables(modelBuilder);
        ConfigureNormalizedTypeTables(modelBuilder);
        ConfigureNormalizedAspectTables(modelBuilder);
        ConfigureNormalizedServiceTables(modelBuilder);
        ConfigureNormalizedViewTables(modelBuilder);
        ConfigureNormalizedAccessControlTables(modelBuilder);
        ConfigureNormalizedRuleTables(modelBuilder);
        ConfigureNormalizedSequenceEventTables(modelBuilder);
        ConfigureNormalizedMigrationDefTables(modelBuilder);
        ConfigureExpressionAstTables(modelBuilder);
        ConfigureBoundOperationTables(modelBuilder);
        ConfigureVersioningTables(modelBuilder);
        ConfigureUpgradeTables(modelBuilder);
    }

    private void ConfigureModuleLifecycleTables(ModelBuilder modelBuilder)
    {
        // Tenant configuration
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(e => e.Id);
            // Allow application-provided UUIDs (e.g., well-known System Tenant ID)
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Subdomain).IsUnique();
            entity.Property(e => e.Settings).HasColumnType("jsonb");
        });

        // Module configuration
        modelBuilder.Entity<Module>(entity =>
        {
            entity.ToTable("modules");
            entity.HasKey(e => e.Id);
            // Allow application-provided UUIDs
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => new { e.TenantId, e.Name, e.Version }).IsUnique();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.Reviewers).HasColumnType("text[]");
            
            entity.HasOne(e => e.Tenant)
                  .WithMany(t => t.Modules)
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(e => e.ExtendsModule)
                  .WithMany()
                  .HasForeignKey(e => e.ExtendsModuleId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ModuleDependency configuration
        modelBuilder.Entity<ModuleDependency>(entity =>
        {
            entity.ToTable("module_dependencies");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ModuleId, e.DependsOnName }).IsUnique();
            
            entity.HasOne(e => e.Module)
                  .WithMany(m => m.Dependencies)
                  .HasForeignKey(e => e.ModuleId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(e => e.ResolvedModule)
                  .WithMany()
                  .HasForeignKey(e => e.ResolvedId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ModuleDeprecation configuration
        modelBuilder.Entity<ModuleDeprecation>(entity =>
        {
            entity.ToTable("module_deprecations");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ModuleId, e.DeprecatedVersion }).IsUnique();
            
            entity.HasOne(e => e.Module)
                  .WithMany(m => m.Deprecations)
                  .HasForeignKey(e => e.ModuleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ModuleInstallation configuration
        modelBuilder.Entity<ModuleInstallation>(entity =>
        {
            entity.ToTable("module_installations");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.ModuleId }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.InstallOrder });
            entity.Property(e => e.Status).HasConversion<string>();
            
            entity.HasOne(e => e.Tenant)
                  .WithMany()
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(e => e.Module)
                  .WithMany()
                  .HasForeignKey(e => e.ModuleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Migration configuration
        modelBuilder.Entity<Migration>(entity =>
        {
            entity.ToTable("migrations");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ModuleId, e.FromVersion, e.ToVersion }).IsUnique();
            entity.Property(e => e.ChangeType).HasConversion<string>();
            entity.Property(e => e.DiffJson).HasColumnType("jsonb");
            
            entity.HasOne(e => e.Module)
                  .WithMany()
                  .HasForeignKey(e => e.ModuleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
    
    // Legacy blob storage configurations REMOVED (ModelPackage, ModelElement, etc.)

    private void ConfigureNormalizedCoreTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Namespace>(e =>
        {
            e.ToTable("namespaces");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<SourceFile>(e =>
        {
            e.ToTable("source_files");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.FilePath }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<NormalizedAnnotation>(e =>
        {
            e.ToTable("annotations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.OwnerType, x.OwnerId, x.Name }).IsUnique();
            e.Property(x => x.Value).HasColumnType("jsonb");
        });
    }

    private void ConfigureNormalizedEntityTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntityRecord>(e =>
        {
            e.ToTable("entities");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.QualifiedName }).IsUnique();
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.ModuleId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Namespace).WithMany(n => n.Entities).HasForeignKey(x => x.NamespaceId);
            e.HasOne(x => x.SourceFile).WithMany().HasForeignKey(x => x.SourceFileId);
            e.HasIndex(x => new { x.TenantId, x.Name });
        });

        modelBuilder.Entity<EntityField>(e =>
        {
            e.ToTable("entity_fields");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntityId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.EntityId, x.Position });
            e.HasOne(x => x.Entity).WithMany(x => x.Fields).HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EntityAssociation>(e =>
        {
            e.ToTable("entity_associations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntityId, x.Name }).IsUnique();
            e.HasIndex(x => x.TargetEntityId);
            e.HasOne(x => x.Entity).WithMany(x => x.Associations).HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetEntity).WithMany().HasForeignKey(x => x.TargetEntityId).OnDelete(DeleteBehavior.SetNull);
        });
        
        modelBuilder.Entity<EntityAspectRef>(e =>
        {
            e.ToTable("entity_aspect_refs");
            e.HasKey(x => new { x.EntityId, x.AspectName });
            e.HasOne(x => x.Entity).WithMany(x => x.AspectRefs).HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<EntityIndex>(e =>
        {
            e.ToTable("entity_indexes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntityId, x.Name }).IsUnique();
            e.HasOne(x => x.Entity).WithMany(x => x.Indexes).HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<EntityIndexField>(e =>
        {
            e.ToTable("entity_index_fields");
            e.HasKey(x => new { x.IndexId, x.FieldName });
            e.HasOne(x => x.Index).WithMany(x => x.Fields).HasForeignKey(x => x.IndexId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<EntityConstraint>(e =>
        {
            e.ToTable("entity_constraints");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntityId, x.Name }).IsUnique();
            e.HasOne(x => x.Entity).WithMany(x => x.Constraints).HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<EntityConstraintField>(e =>
        {
            e.ToTable("entity_constraint_fields");
            e.HasKey(x => new { x.ConstraintId, x.FieldName, x.IsReferenced });
            e.HasOne(x => x.Constraint).WithMany(x => x.Fields).HasForeignKey(x => x.ConstraintId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureNormalizedTypeTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TypeRecord>(e =>
        {
            e.ToTable("types");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.QualifiedName }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Namespace).WithMany(n => n.Types).HasForeignKey(x => x.NamespaceId);
        });
        
        modelBuilder.Entity<TypeField>(e =>
        {
            e.ToTable("type_fields");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TypeId, x.Name }).IsUnique();
            e.HasOne(x => x.Type).WithMany(x => x.Fields).HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<EnumRecord>(e =>
        {
            e.ToTable("enums");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.QualifiedName }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Namespace).WithMany(n => n.Enums).HasForeignKey(x => x.NamespaceId);
        });
        
        modelBuilder.Entity<EnumValue>(e =>
        {
            e.ToTable("enum_values");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EnumId, x.Name }).IsUnique();
            e.HasOne(x => x.Enum).WithMany(x => x.Values).HasForeignKey(x => x.EnumId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureNormalizedAspectTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AspectRecord>(e =>
        {
            e.ToTable("aspects");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.QualifiedName }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Namespace).WithMany(n => n.Aspects).HasForeignKey(x => x.NamespaceId);
        });
        
        modelBuilder.Entity<AspectInclude>(e =>
        {
            e.ToTable("aspect_includes");
            e.HasKey(x => new { x.AspectId, x.IncludedAspectName });
            e.HasOne(x => x.Aspect).WithMany(x => x.Includes).HasForeignKey(x => x.AspectId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<AspectField>(e =>
        {
            e.ToTable("aspect_fields");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AspectId, x.Name }).IsUnique();
            e.HasOne(x => x.Aspect).WithMany(x => x.Fields).HasForeignKey(x => x.AspectId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureNormalizedServiceTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceRecord>(e =>
        {
            e.ToTable("services");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.QualifiedName }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Namespace).WithMany(n => n.Services).HasForeignKey(x => x.NamespaceId);
        });
        
        modelBuilder.Entity<ServiceExposedEntity>(e =>
        {
            e.ToTable("service_exposed_entities");
            e.HasKey(x => new { x.ServiceId, x.EntityName });
            e.HasOne(x => x.Service).WithMany(x => x.ExposedEntities).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Entity).WithMany().HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.SetNull);
        });
        
        modelBuilder.Entity<ServiceOperation>(e =>
        {
            e.ToTable("service_operations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ServiceId, x.Name }).IsUnique();
            e.HasOne(x => x.Service).WithMany(x => x.Operations).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<OperationParameter>(e =>
        {
            e.ToTable("operation_parameters");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.OperationId, x.Name }).IsUnique();
            e.HasOne(x => x.Operation).WithMany(x => x.Parameters).HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<ServiceOperationEmit>(e =>
        {
            e.ToTable("service_operation_emits");
            e.HasKey(x => new { x.OperationId, x.EventName });
            e.HasOne(x => x.Operation).WithMany(x => x.Emits).HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<ServiceEventHandler>(e =>
        {
            e.ToTable("service_event_handlers");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ServiceId, x.EventName }).IsUnique();
            e.HasOne(x => x.Service).WithMany(x => x.EventHandlers).HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.BodyRootStatement).WithMany().HasForeignKey(x => x.BodyRootStatementId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private void ConfigureNormalizedViewTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ViewRecord>(e =>
        {
            e.ToTable("views");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.QualifiedName }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Namespace).WithMany(n => n.Views).HasForeignKey(x => x.NamespaceId);
        });
        
        modelBuilder.Entity<ViewParameter>(e =>
        {
            e.ToTable("view_parameters");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ViewId, x.Name }).IsUnique();
            e.HasOne(x => x.View).WithMany(x => x.Parameters).HasForeignKey(x => x.ViewId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureNormalizedAccessControlTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccessControlRecord>(e =>
        {
            e.ToTable("access_controls");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TargetEntity).WithMany().HasForeignKey(x => x.TargetEntityId).OnDelete(DeleteBehavior.SetNull);
        });
        
        modelBuilder.Entity<AccessRule>(e =>
        {
            e.ToTable("access_rules");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.AccessControl).WithMany(x => x.Rules).HasForeignKey(x => x.AccessControlId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<AccessRuleOperation>(e =>
        {
            e.ToTable("access_rule_operations");
            e.HasKey(x => new { x.RuleId, x.Operation });
            e.HasOne(x => x.Rule).WithMany(x => x.Operations).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<AccessRulePrincipal>(e =>
        {
            e.ToTable("access_rule_principals");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Rule).WithMany(x => x.Principals).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<AccessFieldRestriction>(e =>
        {
            e.ToTable("access_field_restrictions");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Rule).WithMany(x => x.FieldRestrictions).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ConditionExprRoot).WithMany().HasForeignKey(x => x.ConditionExprRootId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private void ConfigureNormalizedRuleTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RuleRecord>(e =>
        {
            e.ToTable("rules");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TargetEntity).WithMany().HasForeignKey(x => x.TargetEntityId).OnDelete(DeleteBehavior.SetNull);
        });
        
        modelBuilder.Entity<RuleTrigger>(e =>
        {
            e.ToTable("rule_triggers");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Rule).WithMany(x => x.Triggers).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<RuleTriggerField>(e =>
        {
            e.ToTable("rule_trigger_fields");
            e.HasKey(x => new { x.TriggerId, x.FieldName });
            e.HasOne(x => x.Trigger).WithMany(x => x.ChangeFields).HasForeignKey(x => x.TriggerId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<RuleStatement>(e =>
        {
            e.ToTable("rule_statements");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Rule).WithMany(x => x.Statements).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ParentStatement).WithMany(x => x.ChildStatements).HasForeignKey(x => x.ParentStatementId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void ConfigureNormalizedSequenceEventTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SequenceRecord>(e =>
        {
            e.ToTable("sequences");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.Name }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ForEntity).WithMany().HasForeignKey(x => x.ForEntityId).OnDelete(DeleteBehavior.SetNull);
        });
        
        modelBuilder.Entity<EventRecord>(e =>
        {
            e.ToTable("events");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.QualifiedName }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Namespace).WithMany(n => n.Events).HasForeignKey(x => x.NamespaceId);
        });
        
        modelBuilder.Entity<EventField>(e =>
        {
            e.ToTable("event_fields");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EventId, x.Name }).IsUnique();
            e.HasOne(x => x.Event).WithMany(x => x.Fields).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureNormalizedMigrationDefTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MigrationDefRecord>(e =>
        {
            e.ToTable("migration_defs");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.QualifiedName }).IsUnique();
            e.Property(x => x.DependenciesJson).HasColumnType("jsonb");
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Namespace).WithMany().HasForeignKey(x => x.NamespaceId);
            e.HasOne(x => x.SourceFile).WithMany().HasForeignKey(x => x.SourceFileId);
        });

        modelBuilder.Entity<MigrationStepRecord>(e =>
        {
            e.ToTable("migration_steps");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.MigrationDefId, x.Direction, x.Position }).IsUnique();
            e.Property(x => x.StepJson).HasColumnType("jsonb");
            e.HasOne(x => x.MigrationDef).WithMany(x => x.Steps).HasForeignKey(x => x.MigrationDefId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureExpressionAstTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExpressionNode>(e =>
        {
            e.ToTable("expression_nodes");
            e.HasKey(x => x.Id);
            
            // Indexes for common queries
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
            e.HasIndex(x => x.NodeType);
            e.HasIndex(x => x.FunctionName);
            e.HasIndex(x => x.AggregateFunction);
            
            // Self-referencing parent-child relationship
            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureBoundOperationTables(ModelBuilder modelBuilder)
    {
        // EntityBoundOperation - actions and functions bound to entities
        modelBuilder.Entity<EntityBoundOperation>(e =>
        {
            e.ToTable("entity_bound_operations");
            e.HasKey(x => x.Id);
            
            e.HasIndex(x => new { x.EntityId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ModuleId });
            e.HasIndex(x => x.BodyDefinitionHash);
            
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Entity).WithMany(x => x.BoundOperations).HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.BodyRootStatement).WithMany().HasForeignKey(x => x.BodyRootStatementId).OnDelete(DeleteBehavior.SetNull);
        });
        
        // BoundOperationParameter - parameters for bound operations
        modelBuilder.Entity<BoundOperationParameter>(e =>
        {
            e.ToTable("bound_operation_parameters");
            e.HasKey(x => x.Id);
            
            e.HasIndex(x => new { x.OperationId, x.Name }).IsUnique();
            
            e.HasOne(x => x.Operation).WithMany(x => x.Parameters).HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
        });
        
        // BoundOperationEmit - events emitted by bound operations (signature-level)
        modelBuilder.Entity<BoundOperationEmit>(e =>
        {
            e.ToTable("bound_operation_emits");
            e.HasKey(x => new { x.OperationId, x.EventName });
            
            e.HasOne(x => x.Operation).WithMany(x => x.Emits).HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Cascade);
        });
        
        // StatementNode - AST nodes for action/function body statements
        modelBuilder.Entity<StatementNode>(e =>
        {
            e.ToTable("statement_nodes");
            e.HasKey(x => x.Id);
            
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
            e.HasIndex(x => x.NodeType);
            
            // Self-referencing parent-child relationship
            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // FK to expression nodes for condition/value expressions
            e.HasOne(x => x.ConditionExprRoot).WithMany().HasForeignKey(x => x.ConditionExprRootId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ValueExprRoot).WithMany().HasForeignKey(x => x.ValueExprRootId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CollectionExprRoot).WithMany().HasForeignKey(x => x.CollectionExprRootId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private void ConfigureVersioningTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ObjectVersion>(e =>
        {
            e.ToTable("object_versions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            
            // Indexes for common queries
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.ObjectType, x.ObjectName });
            e.HasIndex(x => new { x.TenantId, x.ObjectName });
            e.HasIndex(x => new { x.ModuleId, x.Status });
            e.HasIndex(x => x.DefinitionHash);
            
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.DefinitionSnapshot).HasColumnType("jsonb");
            
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<BreakingChange>(e =>
        {
            e.ToTable("breaking_changes");
            e.HasKey(x => x.Id);
            
            e.HasIndex(x => new { x.ObjectVersionId, x.Status });
            e.HasIndex(x => x.Status);
            
            e.Property(x => x.Status).HasConversion<string>();
            
            e.HasOne(x => x.ObjectVersion)
                .WithMany(x => x.BreakingChanges)
                .HasForeignKey(x => x.ObjectVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureUpgradeTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UpgradeWindow>(e =>
        {
            e.ToTable("upgrade_windows");
            e.HasKey(x => x.Id);
            
            e.HasIndex(x => new { x.TenantId, x.ModuleId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.Status });
            
            e.Property(x => x.Status).HasConversion<string>();
            
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Module).WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Cascade);
        });
        
        modelBuilder.Entity<UpgradeSyncStatus>(e =>
        {
            e.ToTable("upgrade_sync_statuses");
            e.HasKey(x => x.Id);
            
            e.HasIndex(x => new { x.WindowId, x.EntityName }).IsUnique();
            e.HasIndex(x => x.Phase);
            
            e.Property(x => x.Phase).HasConversion<string>();
            
            e.HasOne(x => x.Window)
                .WithMany(x => x.SyncStatuses)
                .HasForeignKey(x => x.WindowId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
