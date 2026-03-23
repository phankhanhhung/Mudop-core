using System.Text;
using BMMDL.CodeGen.Generators;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Features;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Services;
using Microsoft.Extensions.Logging;

namespace BMMDL.CodeGen;

/// <summary>
/// Generates PostgreSQL DDL statements from BMMDL metadata.
/// Orchestrates sub-generators for temporal, trigger, view, localization, and file reference DDL.
/// </summary>
public class PostgresDdlGenerator
{
    private readonly MetaModelCache _cache;
    private readonly TypeResolver _resolver;
    private readonly SequenceGenerator _sequenceGenerator;
    private readonly BmModel? _model;
    private readonly Dictionary<string, string> _entityToModuleMap = new();
    private readonly ILogger<PostgresDdlGenerator>? _logger;

    // Sub-generators
    private readonly DdlGeneratorContext _ctx;
    private readonly TemporalDdlGenerator _temporalGen;
    private readonly TriggerDdlGenerator _triggerGen;
    private readonly ViewDdlGenerator _viewGen;
    private readonly LocalizationDdlGenerator _localizationGen;
    private readonly FileReferenceDdlGenerator _fileRefGen;
    private readonly SeedSqlGenerator _seedGen;

    public PostgresDdlGenerator(MetaModelCache cache, ILogger<PostgresDdlGenerator>? logger = null)
    {
        _cache = cache;
        _logger = logger;
        _resolver = new TypeResolver(cache);
        _sequenceGenerator = new SequenceGenerator();
        _ctx = new DdlGeneratorContext(cache, _resolver, null, _entityToModuleMap);
        _temporalGen = new TemporalDdlGenerator(_ctx);
        _triggerGen = new TriggerDdlGenerator(_ctx);
        _viewGen = new ViewDdlGenerator(_ctx);
        _localizationGen = new LocalizationDdlGenerator(_ctx);
        _fileRefGen = new FileReferenceDdlGenerator();
        _seedGen = new SeedSqlGenerator(_ctx);
    }

    /// <summary>
    /// Constructor that accepts a BmModel and creates a MetaModelCache from it.
    /// This is useful for code generation scenarios where you have a compiled model.
    /// </summary>
    public PostgresDdlGenerator(BmModel model, ILogger<PostgresDdlGenerator>? logger = null)
    {
        _model = model;
        _logger = logger;
        _cache = CreateCacheFromModel(model);
        _resolver = new TypeResolver(_cache);
        _sequenceGenerator = new SequenceGenerator();
        BuildEntityModuleMap();
        _ctx = new DdlGeneratorContext(_cache, _resolver, model, _entityToModuleMap);
        _temporalGen = new TemporalDdlGenerator(_ctx);
        _triggerGen = new TriggerDdlGenerator(_ctx);
        _viewGen = new ViewDdlGenerator(_ctx);
        _localizationGen = new LocalizationDdlGenerator(_ctx);
        _fileRefGen = new FileReferenceDdlGenerator();
        _seedGen = new SeedSqlGenerator(_ctx);
    }

    /// <summary>
    /// Creates a MetaModelCache from a BmModel.
    /// </summary>
    private static MetaModelCache CreateCacheFromModel(BmModel model)
    {
        var cache = new MetaModelCache();

        foreach (var entity in model.Entities)
            cache.AddEntity(entity);

        foreach (var type in model.Types)
            cache.AddType(type);

        foreach (var enumType in model.Enums)
            cache.AddEnum(enumType);

        foreach (var aspect in model.Aspects)
            cache.AddAspect(aspect);

        foreach (var service in model.Services)
            cache.AddService(service);

        foreach (var view in model.Views)
            cache.AddView(view);

        cache.MarkInitialized();
        return cache;
    }

    /// <summary>
    /// Build mapping from entity qualified name to module name
    /// </summary>
    private void BuildEntityModuleMap()
    {
        if (_model == null) return;

        foreach (var entity in _model.Entities)
        {
            if (string.IsNullOrEmpty(entity.Namespace)) continue;

            var parts = entity.Namespace.Split('.');
            var schemaName = parts[0];

            _entityToModuleMap[entity.QualifiedName] = schemaName;
        }
    }

    /// <summary>
    /// Get module name for an entity
    /// </summary>
    private string? GetModuleNameForEntity(BmEntity entity)
    {
        if (_entityToModuleMap.TryGetValue(entity.QualifiedName, out var moduleName))
            return moduleName;
        return null;
    }

    /// <summary>
    /// Get fully qualified table name for an entity (schema.table), with each part quoted.
    /// </summary>
    private string GetQualifiedTableNameForEntity(BmEntity entity)
    {
        var moduleName = GetModuleNameForEntity(entity);
        return QuoteQualifiedName(NamingConvention.GetQualifiedTableName(entity, moduleName));
    }

    /// <summary>
    /// Get unqualified table name (for backwards compat)
    /// </summary>
    private string GetUnqualifiedTableName(BmEntity entity)
    {
        return NamingConvention.GetTableName(entity);
    }

    /// <summary>
    /// Quote a potentially qualified name (schema.table → "schema"."table", or table → "table").
    /// </summary>
    private static string QuoteQualifiedName(string qualifiedName)
    {
        var dotIndex = qualifiedName.IndexOf('.');
        if (dotIndex >= 0)
        {
            var schema = qualifiedName[..dotIndex];
            var name = qualifiedName[(dotIndex + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(name)}";
        }
        return NamingConvention.QuoteIdentifier(qualifiedName);
    }

    /// <summary>
    /// Generate CREATE SCHEMA statements for all modules
    /// </summary>
    private string GenerateModuleSchemas()
    {
        if (_model == null || _model.AllModules.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Module Schemas");
        sb.AppendLine("-- ============================================");
        sb.AppendLine();

        var schemas = _model.AllModules
            .Select(m => NamingConvention.GetSchemaName(m.Name))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s);

        foreach (var schema in schemas)
        {
            sb.AppendLine($"CREATE SCHEMA IF NOT EXISTS {NamingConvention.QuoteIdentifier(schema)};");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Generate complete schema DDL for all entities
    /// </summary>
    public string GenerateSchema(string[] entityTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- BMMDL Generated Schema");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("-- ============================================");
        sb.AppendLine();

        // Generate module schemas first
        sb.Append(GenerateModuleSchemas());

        // Check if any entity uses temporal - requires btree_gist extension
        var hasTemporalEntities = entityTypes.Any(t =>
            _cache.Entities.TryGetValue(t, out var e) && e.IsTemporal);
        if (hasTemporalEntities)
        {
            sb.AppendLine("-- ============================================");
            sb.AppendLine("-- Temporal Prerequisites");
            sb.AppendLine("-- ============================================");
            sb.AppendLine();
            sb.AppendLine("-- Required for EXCLUDE constraints with temporal ranges");
            sb.AppendLine("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            sb.AppendLine();
        }

        // Generate sequence infrastructure first
        sb.AppendLine(_sequenceGenerator.GenerateAllSequenceInfrastructure());
        sb.AppendLine();

        // Generate CREATE SEQUENCE statements for model-defined sequences
        if (_model != null && _model.Sequences.Count > 0)
        {
            sb.AppendLine("-- ============================================");
            sb.AppendLine("-- Model-Defined Sequences");
            sb.AppendLine("-- ============================================");
            sb.AppendLine();
            foreach (var seq in _model.Sequences)
            {
                sb.AppendLine(GenerateSequenceDdl(seq));
                sb.AppendLine();
            }
        }

        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Entity Tables");
        sb.AppendLine("-- ============================================");
        sb.AppendLine();

        // Sort entities so parent tables are created before child tables (inheritance hierarchy)
        var sortedEntityTypes = TopologicallySortEntities(entityTypes);

        foreach (var entityType in sortedEntityTypes)
        {
            if (_cache.Entities.TryGetValue(entityType, out var entity))
            {
                var isAbstract = entity.IsAbstract || entity.HasAnnotation("Abstract");
                if (isAbstract && entity.DerivedEntities.Count == 0)
                    continue;

                sb.AppendLine(GenerateTable(entity));
                sb.AppendLine();
            }
        }

        // Generate junction tables for Many-to-Many associations
        var junctionTables = new HashSet<string>();
        foreach (var entityType in entityTypes)
        {
            if (_cache.Entities.TryGetValue(entityType, out var entity))
            {
                foreach (var assoc in entity.Associations.Where(a => a.Cardinality == BmCardinality.ManyToMany))
                {
                    var junctionDdl = GenerateJunctionTable(entity, assoc, junctionTables);
                    if (!string.IsNullOrEmpty(junctionDdl))
                    {
                        sb.AppendLine(junctionDdl);
                        sb.AppendLine();
                    }
                }
            }
        }

        // Generate _texts companion tables for entities with localized fields
        foreach (var entityType in entityTypes)
        {
            if (_cache.Entities.TryGetValue(entityType, out var entity))
            {
                var textsDdl = GenerateTextsTable(entity);
                if (!string.IsNullOrEmpty(textsDdl))
                {
                    sb.AppendLine(textsDdl);
                    sb.AppendLine();
                }
            }
        }

        // Generate triggers for computed fields with Trigger strategy
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Computed Field Triggers");
        sb.AppendLine("-- ============================================");
        sb.AppendLine();

        foreach (var entityType in entityTypes)
        {
            if (_cache.Entities.TryGetValue(entityType, out var entity))
            {
                var triggers = GenerateAllComputedFieldTriggers(entity);
                if (!string.IsNullOrWhiteSpace(triggers))
                {
                    sb.AppendLine(triggers);
                    sb.AppendLine();
                }
            }
        }

        // Generate cross-aspect views
        var crossAspectViews = GenerateCrossAspectViews();
        if (!string.IsNullOrWhiteSpace(crossAspectViews))
        {
            sb.AppendLine("-- ============================================");
            sb.AppendLine("-- Cross-Aspect Views");
            sb.AppendLine("-- ============================================");
            sb.AppendLine();
            sb.Append(crossAspectViews);
        }

        // Generate BMMDL-defined views (must come after all tables they reference)
        var viewsDdl = GenerateAllViews();
        if (!string.IsNullOrWhiteSpace(viewsDdl))
        {
            sb.AppendLine();
            sb.Append(viewsDdl);
        }

        // Seed Data
        if (_model != null && _model.Seeds.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("-- ============================================");
            sb.AppendLine("-- Seed Data");
            sb.AppendLine("-- ============================================");
            sb.AppendLine();
            sb.Append(_seedGen.GenerateAllSeedSql(_model));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generate complete schema DDL for all entities in the cache.
    /// This is a convenience method that automatically includes all entities.
    /// </summary>
    public string GenerateFullSchema()
    {
        var allEntityTypes = _cache.Entities.Keys.ToArray();
        return GenerateSchema(allEntityTypes);
    }

    // ============================================================
    // View DDL — delegated to ViewDdlGenerator
    // ============================================================

    /// <summary>
    /// Generate CREATE VIEW statement for a single view.
    /// </summary>
    public string GenerateCreateView(BmView view) => _viewGen.GenerateCreateView(view);

    /// <summary>
    /// Generate all views in the model.
    /// </summary>
    public string GenerateAllViews() => _viewGen.GenerateAllViews();

    /// <summary>
    /// Generate UNION ALL views for aspects annotated with @Query.CrossAspect.
    /// </summary>
    public string GenerateCrossAspectViews() => _viewGen.GenerateCrossAspectViews();

    // ============================================================
    // Table DDL — core table generation with delegation to sub-generators
    // ============================================================

    /// <summary>
    /// Generate CREATE TABLE statement for a single entity
    /// </summary>
    public string GenerateTable(BmEntity entity)
    {
        var tableName = GetQualifiedTableNameForEntity(entity);
        var sb = new StringBuilder();

        // Add source location comment for BMMDL -> SQL traceability
        if (!string.IsNullOrEmpty(entity.SourceFile))
        {
            sb.AppendLine($"-- Source: {Path.GetFileName(entity.SourceFile)}:{entity.StartLine}");
        }

        // Add tenant isolation comment
        if (entity.TenantScoped)
        {
            sb.AppendLine($"-- Table for {entity.Namespace}.{entity.Name} (TENANT-SCOPED)");
            sb.AppendLine($"-- Tenant isolation enforced at application and database level");
        }
        else
        {
            sb.AppendLine($"-- Table for {entity.Namespace}.{entity.Name}");
        }

        sb.AppendLine($"CREATE TABLE {tableName} (");

        var columns = new List<string>();
        var constraints = new List<string>();

        // Table-per-type inheritance: discriminator/child-id columns from InheritanceFeature
        if (entity.Features.TryGetValue("Inheritance", out var inheritanceMeta))
        {
            foreach (var col in inheritanceMeta.Columns)
            {
                var nullable = col.Nullable ? "" : " NOT NULL";
                var def = col.DefaultExpr != null ? $" DEFAULT {col.DefaultExpr}" : "";
                var inlinePk = (col.Name == "id" && entity.ParentEntity != null) ? " PRIMARY KEY" : "";
                columns.Add($"    {NamingConvention.QuoteIdentifier(col.Name)} {col.SqlType}{nullable}{def}{inlinePk}");
            }
        }

        // Table-per-type inheritance: child entity with parent reference
        var parentFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (entity.ParentEntity != null)
        {
            CollectInheritedFieldNames(entity.ParentEntity, parentFieldNames);
        }

        // Generate columns for fields
        foreach (var field in entity.Fields)
        {
            if (parentFieldNames.Contains(field.Name))
                continue;

            var sourceComment = (!string.IsNullOrEmpty(field.SourceFile) && field.StartLine > 0)
                ? $"    -- Source: {Path.GetFileName(field.SourceFile)}:{field.StartLine}\n"
                : "";

            // FileReference fields — delegated to FileReferenceDdlGenerator
            if (field.TypeRef is BmFileReferenceType fileRefType)
            {
                var metadataColumns = _fileRefGen.GenerateFileReferenceColumns(field, fileRefType);
                for (var i = 0; i < metadataColumns.Count; i++)
                {
                    var prefix = i == 0 ? sourceComment : "";
                    columns.Add($"{prefix}    {metadataColumns[i]}");
                }

                var storageConstraints = _fileRefGen.GenerateFileReferenceConstraints(field, fileRefType);
                constraints.AddRange(storageConstraints.Select(c => $"    {c}"));
            }
            else
            {
                var resolved = _resolver.Resolve(field);

                if (resolved.FlattenedFields != null)
                {
                    for (var i = 0; i < resolved.FlattenedFields.Count; i++)
                    {
                        var prefix = i == 0 ? sourceComment : "";
                        columns.Add($"{prefix}    {resolved.FlattenedFields[i].ToColumnDefinition()}");
                    }
                }
                else
                {
                    var columnDef = GenerateColumn(field, entity);
                    columns.Add($"{sourceComment}    {columnDef}");

                    foreach (var constraint in resolved.Constraints)
                    {
                        constraints.Add($"    {constraint}");
                    }
                }
            }
        }

        // Expand aspect fields inline (skip if already inlined by OptimizationPass)
        var existingFieldNames = new HashSet<string>(
            entity.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var aspectName in entity.Aspects)
        {
            if (_cache.Aspects.TryGetValue(aspectName, out var aspect))
            {
                foreach (var field in aspect.Fields)
                {
                    if (!existingFieldNames.Contains(field.Name))
                    {
                        var columnDef = GenerateColumn(field, entity);
                        columns.Add($"    {columnDef}");
                        existingFieldNames.Add(field.Name);
                    }
                    else
                    {
                        _logger?.LogWarning(
                            "Aspect '{AspectName}' field '{FieldName}' conflicts with existing field on entity '{EntityName}' — skipped",
                            aspectName, field.Name, entity.Name);
                    }
                }
            }
        }

        // Temporal columns from TemporalFeature
        if (entity.Features.TryGetValue("Temporal", out var temporalMeta))
        {
            foreach (var col in temporalMeta.Columns)
            {
                var nullable = col.Nullable ? "" : " NOT NULL";
                var def = col.DefaultExpr != null ? $" DEFAULT {col.DefaultExpr}" : "";
                columns.Add($"    {NamingConvention.QuoteIdentifier(col.Name)} {col.SqlType}{nullable}{def}");
            }
        }

        // HasStream: Media stream columns from HasStreamFeature
        if (entity.Features.TryGetValue("HasStream", out var hasStreamMeta))
        {
            foreach (var col in hasStreamMeta.Columns)
            {
                var nullable = col.Nullable ? "" : " NOT NULL";
                var def = col.DefaultExpr != null ? $" DEFAULT {col.DefaultExpr}" : "";
                columns.Add($"    {NamingConvention.QuoteIdentifier(col.Name)} {col.SqlType}{nullable}{def}");
            }
        }

        // Generate foreign keys for associations (skip ManyToMany)
        foreach (var assoc in entity.Associations)
        {
            if (assoc.Cardinality == BmCardinality.ManyToMany)
                continue;

            var fkColumn = _ctx.GenerateForeignKeyColumn(assoc);
            columns.Add($"    {fkColumn}");
        }

        // Generate parent FK columns for composition targets (deduplicate to avoid duplicate columns)
        var addedParentFkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parentEntity in _cache.Entities.Values)
        {
            foreach (var comp in parentEntity.Compositions)
            {
                if (string.Equals(comp.TargetEntity, entity.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(comp.TargetEntity, entity.QualifiedName, StringComparison.OrdinalIgnoreCase))
                {
                    var parentFkColumn = NamingConvention.GetFkColumnName(parentEntity.Name);
                    if (addedParentFkColumns.Add(parentFkColumn))
                    {
                        columns.Add($"    {NamingConvention.QuoteIdentifier(parentFkColumn)} UUID NOT NULL");
                    }
                }
            }
        }

        foreach (var constraint in entity.Constraints)
        {
            var constraintDef = GenerateConstraint(tableName, constraint, entity);
            if (!string.IsNullOrEmpty(constraintDef))
            {
                constraints.Add($"    {constraintDef}");
            }
        }

        // Primary key and temporal constraints
        if (entity.Features.TryGetValue("Temporal", out var temporalConstraintMeta))
        {
            // Temporal PK + EXCLUDE constraints from TemporalFeature
            var keyFields = entity.Fields.Where(f => f.IsKey)
                .Select(f => NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(f.Name))).ToList();

            if (keyFields.Count > 0)
            {
                var primaryKeyColumns = string.Join(", ", keyFields);

                if (entity.TemporalStrategy == TemporalStrategy.InlineHistory)
                {
                    if (entity.HasValidTime && !string.IsNullOrEmpty(entity.ValidTimeFromColumn))
                    {
                        var validFromCol = NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(entity.ValidTimeFromColumn));
                        constraints.Add($"    PRIMARY KEY ({primaryKeyColumns}, {validFromCol}, {NamingConvention.QuoteIdentifier("system_start")})");
                    }
                    else
                    {
                        constraints.Add($"    PRIMARY KEY ({primaryKeyColumns}, {NamingConvention.QuoteIdentifier("system_start")})");
                    }
                }
                else
                {
                    constraints.Add($"    PRIMARY KEY ({primaryKeyColumns})");
                }
            }

            foreach (var fc in temporalConstraintMeta.Constraints)
            {
                constraints.Add($"    {fc.Definition}");
            }
        }
        else if (entity.ParentEntity == null)
        {
            // Standard primary key for non-temporal, non-child entities
            var keyFields = entity.Fields.Where(f => f.IsKey).Select(f => NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(f.Name))).ToList();
            if (keyFields.Count > 0)
            {
                var primaryKeyColumns = string.Join(", ", keyFields);
                constraints.Add($"    PRIMARY KEY ({primaryKeyColumns})");
            }
        }

        // Table-per-type inheritance: child table FK to parent from InheritanceFeature
        if (entity.Features.TryGetValue("Inheritance", out var inheritanceConstraintMeta))
        {
            foreach (var fc in inheritanceConstraintMeta.Constraints)
            {
                constraints.Add($"    {fc.Definition}");
            }
        }

        // Combine columns and constraints
        var allDefinitions = columns.Concat(constraints);
        sb.AppendLine(string.Join(",\n", allDefinitions));

        sb.AppendLine(");");

        // Generate indexes (skip if a unique constraint already covers the same columns)
        var uniqueConstraintColumnSets = entity.Constraints
            .OfType<BmUniqueConstraint>()
            .Select(uc => string.Join(",", uc.Fields.Select(f => f.ToLowerInvariant()).OrderBy(f => f)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var index in entity.Indexes)
        {
            var indexColumnKey = string.Join(",", index.Fields.Select(f => f.ToLowerInvariant()).OrderBy(f => f));
            if (uniqueConstraintColumnSets.Contains(indexColumnKey))
                continue;

            sb.AppendLine();
            sb.AppendLine(GenerateIndex(tableName, index, entity));
        }

        // Table-per-type inheritance: discriminator index from InheritanceFeature
        if (entity.Features.TryGetValue("Inheritance", out var inheritanceIndexMeta))
        {
            foreach (var fi in inheritanceIndexMeta.Indexes)
            {
                if (fi.Columns.Length == 0) continue;
                var indexColumns = string.Join(", ", fi.Columns.Select(NamingConvention.QuoteIdentifier));
                var unique = fi.Unique ? "UNIQUE " : "";
                var unqualifiedForIndex = GetUnqualifiedTableName(entity);
                var indexName = NamingConvention.QuoteIdentifier($"idx_{unqualifiedForIndex}_{fi.Columns[0]}");
                sb.AppendLine();
                sb.AppendLine($"-- Inheritance discriminator index");
                sb.AppendLine($"CREATE {unique}INDEX {indexName} ON {tableName}({indexColumns});");
            }
        }

        // Tenant isolation index + RLS from TenantIsolationFeature
        if (entity.Features.TryGetValue("TenantIsolation", out var tenantMeta))
        {
            var unqualifiedForIndex = GetUnqualifiedTableName(entity);
            foreach (var fi in tenantMeta.Indexes)
            {
                if (fi.Columns == null || fi.Columns.Length == 0) continue;
                var indexColumns = string.Join(", ", fi.Columns.Select(NamingConvention.QuoteIdentifier));
                sb.AppendLine();
                sb.AppendLine($"-- Tenant isolation index");
                sb.AppendLine($"CREATE INDEX {NamingConvention.QuoteIdentifier($"idx_{unqualifiedForIndex}_tenant")} ON {tableName}({indexColumns});");
            }

            foreach (var stmt in tenantMeta.PostTableStatements)
            {
                var resolved = stmt.Replace("{TABLE}", tableName);
                sb.AppendLine();
                if (resolved.Contains("ENABLE ROW LEVEL SECURITY"))
                    sb.AppendLine("-- Enable Row-Level Security");
                else if (resolved.Contains("CREATE POLICY"))
                    sb.AppendLine("-- Tenant isolation policy");
                sb.AppendLine(resolved);
            }
        }

        // Sequence trigger from SequenceFeature
        if (entity.Features.TryGetValue("Sequence", out var seqMeta))
        {
            foreach (var stmt in seqMeta.PostTableStatements)
            {
                var resolved = stmt.Replace("{TABLE}", tableName);
                sb.AppendLine();
                sb.AppendLine(resolved);
            }
        }

        // Temporal post-table DDL (indexes, history tables, triggers) from TemporalFeature
        if (entity.Features.TryGetValue("Temporal", out var temporalPostMeta))
        {
            var unqualifiedTable = GetUnqualifiedTableName(entity);
            foreach (var fi in temporalPostMeta.Indexes)
            {
                if (fi.Columns == null || fi.Columns.Length == 0) continue;
                var indexColumns = string.Join(", ", fi.Columns.Select(NamingConvention.QuoteIdentifier));
                var where = fi.Where != null ? $" WHERE {fi.Where}" : "";
                sb.AppendLine();
                sb.AppendLine($"-- Temporal: Partial index for current records lookup");
                sb.AppendLine($"CREATE INDEX {NamingConvention.QuoteIdentifier($"idx_{unqualifiedTable}_current")} ON {tableName}({indexColumns}){where};");
            }

            foreach (var stmt in temporalPostMeta.PostTableStatements)
            {
                var resolved = stmt.Replace("{TABLE}", tableName);
                sb.AppendLine();
                sb.AppendLine(resolved);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Sorts entity types so parent entities come before child entities in the inheritance hierarchy.
    /// Non-inheritance entities preserve their original order.
    /// </summary>
    private string[] TopologicallySortEntities(string[] entityTypes)
    {
        var result = new List<string>(entityTypes.Length);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddWithParents(string entityType)
        {
            if (!added.Add(entityType))
                return;

            if (_cache.Entities.TryGetValue(entityType, out var entity) && entity.ParentEntity != null)
            {
                // Ensure parent is added first
                var parentKey = _cache.Entities.Keys
                    .FirstOrDefault(k => string.Equals(k, entity.ParentEntity.Name, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(k, entity.ParentEntity.QualifiedName, StringComparison.OrdinalIgnoreCase));
                if (parentKey != null)
                    AddWithParents(parentKey);
            }

            result.Add(entityType);
        }

        foreach (var entityType in entityTypes)
            AddWithParents(entityType);

        return result.ToArray();
    }

    /// <summary>
    /// Collects all field names from the parent entity chain (for table-per-type inheritance).
    /// </summary>
    private void CollectInheritedFieldNames(BmEntity parent, HashSet<string> fieldNames)
    {
        foreach (var field in parent.Fields)
        {
            fieldNames.Add(field.Name);
        }
        if (parent.ParentEntity != null)
        {
            CollectInheritedFieldNames(parent.ParentEntity, fieldNames);
        }
    }

    /// <summary>
    /// Generate a junction table for Many-to-Many associations.
    /// </summary>
    private string GenerateJunctionTable(BmEntity sourceEntity, BmAssociation assoc, HashSet<string> generated)
    {
        var targetEntity = _cache.Entities.Values
            .FirstOrDefault(e => string.Equals(e.Name, assoc.TargetEntity, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(e.QualifiedName, assoc.TargetEntity, StringComparison.OrdinalIgnoreCase));

        if (targetEntity == null)
            return "";

        var names = new[] { sourceEntity.Name.ToLowerInvariant(), targetEntity.Name.ToLowerInvariant() };
        Array.Sort(names);
        var junctionName = $"{names[0]}_{names[1]}";

        if (!generated.Add(junctionName))
            return "";

        var schema = !string.IsNullOrEmpty(sourceEntity.Namespace)
            ? NamingConvention.GetSchemaName(sourceEntity.Namespace)
            : null;
        var qualifiedJunction = schema != null ? $"\"{schema}\".\"{junctionName}\"" : $"\"{junctionName}\"";

        var sourceFk = NamingConvention.GetFkColumnName(sourceEntity.Name);
        var targetFk = NamingConvention.GetFkColumnName(targetEntity.Name);

        var quotedSourceFk = NamingConvention.QuoteIdentifier(sourceFk);
        var quotedTargetFk = NamingConvention.QuoteIdentifier(targetFk);

        var sb = new StringBuilder();
        sb.AppendLine($"-- Junction table for M:M between {sourceEntity.Name} and {targetEntity.Name}");
        sb.AppendLine($"CREATE TABLE {qualifiedJunction} (");
        sb.AppendLine("    \"id\" UUID PRIMARY KEY DEFAULT gen_random_uuid(),");
        sb.AppendLine($"    {quotedSourceFk} UUID NOT NULL,");
        sb.AppendLine($"    {quotedTargetFk} UUID NOT NULL,");

        if (sourceEntity.TenantScoped || targetEntity.TenantScoped)
        {
            sb.AppendLine("    \"tenant_id\" UUID NOT NULL,");
        }

        sb.AppendLine($"    UNIQUE ({quotedSourceFk}, {quotedTargetFk})");
        sb.AppendLine(");");

        return sb.ToString();
    }

    // ============================================================
    // Localization — delegated to LocalizationDdlGenerator
    // ============================================================

    /// <summary>
    /// Generate a companion _texts table for entities with localized fields.
    /// </summary>
    private string? GenerateTextsTable(BmEntity entity) => _localizationGen.GenerateTextsTable(entity);

    /// <summary>
    /// Generate table for composition (child entities)
    /// </summary>
    public string GenerateCompositionTable(BmEntity parentEntity, BmComposition composition)
    {
        if (!_cache.Entities.TryGetValue(composition.TargetEntity, out var childEntity))
        {
            return $"-- Warning: Composition target {composition.TargetEntity} not found";
        }

        var sb = new StringBuilder();
        var tableName = NamingConvention.QuoteIdentifier(NamingConvention.GetTableName(childEntity));
        var parentFkColumn = NamingConvention.QuoteIdentifier(NamingConvention.GetFkColumnName(parentEntity.Name));

        sb.AppendLine($"-- Composition table for {childEntity.Namespace}.{childEntity.Name}");
        sb.AppendLine($"CREATE TABLE {tableName} (");

        var columns = new List<string>();
        var constraints = new List<string>();

        columns.Add($"    {parentFkColumn} UUID NOT NULL");

        foreach (var field in childEntity.Fields)
        {
            var columnDef = GenerateColumn(field, childEntity);
            columns.Add($"    {columnDef}");
        }

        var allDefinitions = columns.Concat(constraints);
        sb.AppendLine(string.Join(",\n", allDefinitions));
        sb.AppendLine(");");

        return sb.ToString();
    }

    // ============================================================
    // Column generation — stays in the main class (used by GenerateTable and GenerateCompositionTable)
    // ============================================================

    private string GenerateColumn(BmField field, BmEntity entity)
    {
        var columnName = NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(field.Name));
        var resolved = _resolver.Resolve(field);

        if (field.IsComputed && field.ComputedExpr != null)
        {
            var strategy = field.ComputedStrategy ?? BMMDL.MetaModel.Enums.ComputedStrategy.Stored;

            switch (strategy)
            {
                case BMMDL.MetaModel.Enums.ComputedStrategy.Stored:
                    var translator = new ExpressionTranslator(entity);
                    var expression = translator.Translate(field.ComputedExpr);
                    return $"{columnName} {resolved.PostgresType} GENERATED ALWAYS AS ({expression}) STORED";

                case BMMDL.MetaModel.Enums.ComputedStrategy.Virtual:
                    var nullConstraintVirtual = field.IsKey || !resolved.Nullable ? " NOT NULL" : "";
                    return $"{columnName} {resolved.PostgresType}{nullConstraintVirtual}";

                case BMMDL.MetaModel.Enums.ComputedStrategy.Application:
                    var nullConstraint = field.IsKey || !resolved.Nullable ? " NOT NULL" : "";
                    return $"{columnName} {resolved.PostgresType}{nullConstraint}";

                case BMMDL.MetaModel.Enums.ComputedStrategy.Trigger:
                    var nullConstraintTrigger = field.IsKey || !resolved.Nullable ? " NOT NULL" : "";
                    return $"{columnName} {resolved.PostgresType}{nullConstraintTrigger}";

                default:
                    goto case BMMDL.MetaModel.Enums.ComputedStrategy.Stored;
            }
        }

        var seqName = field.GetAnnotation("Sequence.Name")?.Value as string;
        if (!string.IsNullOrEmpty(seqName))
        {
            var snakeSeqName = NamingConvention.ToSnakeCase(seqName);
            // Build schema-qualified sequence name for nextval() string literal
            var moduleName = GetModuleNameForEntity(entity);
            string? seqSchema = null;
            if (!string.IsNullOrEmpty(moduleName))
                seqSchema = NamingConvention.GetSchemaName(moduleName);
            else if (!string.IsNullOrEmpty(entity.Namespace))
                seqSchema = NamingConvention.GetSchemaName(entity.Namespace);

            // nextval() takes a regclass string literal — use schema-qualified "schema"."name" inside single quotes
            // Escape any embedded single quotes in the identifier parts
            var escapedSeqName = snakeSeqName.Replace("'", "''").Replace("\"", "\"\"");
            var nextvalArg = seqSchema != null
                ? $"\"{ seqSchema.Replace("'", "''").Replace("\"", "\"\"") }\".\"{escapedSeqName}\""
                : $"\"{escapedSeqName}\"";
            return resolved.ToColumnDefinition(columnName) + $" DEFAULT nextval('{nextvalArg}')";
        }

        return resolved.ToColumnDefinition(columnName);
    }

    // ============================================================
    // Trigger DDL — delegated to TriggerDdlGenerator
    // ============================================================

    /// <summary>
    /// Generate BEFORE INSERT trigger for sequence fields
    /// </summary>
    public string GenerateSequenceTrigger(BmEntity entity) => _triggerGen.GenerateSequenceTrigger(entity);

    /// <summary>
    /// Generate trigger function and trigger for computed fields that use Trigger strategy.
    /// </summary>
    public string GenerateComputedFieldTrigger(BmEntity entity, BmField computedField)
        => _triggerGen.GenerateComputedFieldTrigger(entity, computedField);

    /// <summary>
    /// Generate all triggers for computed fields with Trigger strategy in an entity.
    /// </summary>
    public string GenerateAllComputedFieldTriggers(BmEntity entity)
        => _triggerGen.GenerateAllComputedFieldTriggers(entity);

    // ============================================================
    // Temporal DDL — delegated to TemporalDdlGenerator
    // ============================================================

    /// <summary>
    /// Generate history table for temporal entity with Separate Tables strategy.
    /// </summary>
    public string GenerateTemporalHistoryTable(BmEntity entity)
        => _temporalGen.GenerateTemporalHistoryTable(entity);

    /// <summary>
    /// Generate versioning trigger for temporal entity with Separate Tables strategy.
    /// </summary>
    public string GenerateTemporalVersioningTrigger(BmEntity entity)
        => _temporalGen.GenerateTemporalVersioningTrigger(entity);

    // ============================================================
    // Index and constraint generation — stays in the main class
    // ============================================================

    private string GenerateIndex(string tableName, BmIndex index, BmEntity entity)
    {
        var indexName = NamingConvention.QuoteIdentifier(NamingConvention.GetIndexName(index.Name));
        var unique = index.IsUnique ? "UNIQUE " : "";

        if (!string.IsNullOrEmpty(index.Expression))
        {
            return $"CREATE {unique}INDEX {indexName} ON {tableName}({index.Expression});";
        }

        var columnNames = index.Fields.Select(fieldName =>
        {
            var isAssociation = entity.Associations.Any(a =>
                a.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            if (isAssociation)
            {
                return NamingConvention.QuoteIdentifier(NamingConvention.GetFkColumnName(fieldName));
            }
            else
            {
                return NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(fieldName));
            }
        });

        var columns = string.Join(", ", columnNames);
        return $"CREATE {unique}INDEX {indexName} ON {tableName}({columns});";
    }

    private string GenerateConstraint(string tableName, BmConstraint constraint, BmEntity entity)
    {
        return constraint switch
        {
            BmCheckConstraint checkConstraint => GenerateCheckConstraint(checkConstraint, entity),
            BmUniqueConstraint uniqueConstraint => GenerateUniqueConstraint(uniqueConstraint),
            BmForeignKeyConstraint => string.Empty,
            _ => string.Empty
        };
    }

    private string GenerateCheckConstraint(BmCheckConstraint constraint, BmEntity entity)
    {
        var constraintName = NamingConvention.QuoteIdentifier(NamingConvention.GetCheckConstraintName(constraint.Name));

        if (constraint.Condition == null)
        {
            throw new InvalidOperationException(
                $"Constraint '{constraint.Name}' requires a parsed condition AST. Raw condition strings are not allowed.");
        }

        var translator = new ExpressionTranslator(entity);
        var condition = translator.Translate(constraint.Condition);

        return $"CONSTRAINT {constraintName} CHECK ({condition})";
    }

    private string GenerateUniqueConstraint(BmUniqueConstraint constraint)
    {
        var constraintName = NamingConvention.QuoteIdentifier(NamingConvention.GetUniqueConstraintName(constraint.Name));
        var columns = string.Join(", ", constraint.Fields.Select(f => NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(f))));

        return $"CONSTRAINT {constraintName} UNIQUE ({columns})";
    }

    // ============================================================
    // Sequence DDL Generation
    // ============================================================

    /// <summary>
    /// Generate CREATE SEQUENCE DDL for a BmSequence definition.
    /// </summary>
    public string GenerateSequenceDdl(BmSequence seq)
    {
        var sb = new StringBuilder();
        var seqName = NamingConvention.GetColumnName(seq.Name);

        string? schema = null;
        if (!string.IsNullOrEmpty(seq.ForEntity))
        {
            var entity = _cache.Entities.Values.FirstOrDefault(e =>
                string.Equals(e.Name, seq.ForEntity, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.QualifiedName, seq.ForEntity, StringComparison.OrdinalIgnoreCase));
            if (entity != null)
            {
                var moduleName = GetModuleNameForEntity(entity);
                if (!string.IsNullOrEmpty(moduleName))
                    schema = NamingConvention.GetSchemaName(moduleName);
                else if (!string.IsNullOrEmpty(entity.Namespace))
                    schema = NamingConvention.GetSchemaName(entity.Namespace);
            }
        }

        var quotedSeqName = NamingConvention.QuoteIdentifier(seqName);
        var qualifiedSeqName = schema != null
            ? $"{NamingConvention.QuoteIdentifier(schema)}.{quotedSeqName}"
            : quotedSeqName;

        sb.AppendLine($"-- Sequence: {seq.Name}");
        sb.Append($"CREATE SEQUENCE IF NOT EXISTS {qualifiedSeqName}");
        sb.Append($" INCREMENT BY {seq.Increment}");
        sb.Append($" MINVALUE {seq.StartValue}");

        if (seq.MaxValue.HasValue)
        {
            sb.Append($" MAXVALUE {seq.MaxValue.Value}");
        }
        else
        {
            sb.Append(" NO MAXVALUE");
        }

        sb.Append($" START WITH {seq.StartValue}");
        sb.AppendLine(";");

        return sb.ToString();
    }
}
