using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BMMDL.CodeGen;
using BMMDL.CodeGen.Schema;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BMMDL.SchemaManager;

/// <summary>
/// PostgreSQL implementation of ISchemaManager.
/// Generates and applies DDL from BmModel.
/// </summary>
public class PostgresSchemaManager : ISchemaManager
{
    private readonly SchemaManagerOptions _options;
    private readonly ILogger _logger;

    public PostgresSchemaManager(SchemaManagerOptions options)
    {
        options.Validate();
        _options = options;
        _logger = options.Logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public async Task<SchemaOperationResult> InitializeSchemaAsync(
        BmModel model,
        bool force = false,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Step 1: Check if schema already exists
            var schemaReader = new PostgresSchemaReader(_options.ConnectionString);
            var existingSchema = await schemaReader.ReadSchemaAsync();

            if (existingSchema.Tables.Count > 0 && !force)
            {
                return SchemaOperationResult.Fail(
                    $"Schema already contains {existingSchema.Tables.Count} table(s). Clear manually or use MigrateSchemaAsync for updates.");
            }

            // Step 2: Generate DDL from model
            var ddlGenerator = new PostgresDdlGenerator(model);
            var ddl = ddlGenerator.GenerateFullSchema();

            if (string.IsNullOrWhiteSpace(ddl))
            {
                _logger.LogWarning("No entities to generate DDL for");
                return SchemaOperationResult.Ok(0, null, "");
            }

            if (_options.Verbose)
                _logger.LogDebug("Generated DDL:\n{Ddl}", ddl);

            // Step 3: Create migration record
            var migrationName = $"Initial_{DateTime.UtcNow:yyyyMMddHHmmss}";
            var migration = new Migration
            {
                Name = migrationName,
                UpScript = ddl,
                DownScript = GenerateDropScript(model),
                Checksum = ComputeChecksum(ddl),
                Timestamp = DateTime.UtcNow
            };

            // Step 4: Preview if dry-run
            if (dryRun)
            {
                sw.Stop();
                return new SchemaOperationResult
                {
                    Success = true,
                    TablesAffected = CountTables(model),
                    MigrationName = migrationName,
                    GeneratedDdl = ddl,
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }

            // Schema initialization NEVER drops tables - it only creates!
            // If you need to drop, use MigrateSchemaAsync or manual cleanup
            
            // Step 5: Execute migration
            _logger.LogInformation("Executing DDL...");
            var migrationExecutor = new MigrationExecutor(_options.ConnectionString);
            var result = await migrationExecutor.ApplyMigrationAsync(migration);

            sw.Stop();
            
            if (result.Success)
            {
                _logger.LogInformation("Schema initialized successfully. Tables: {Count}, Migration: {Name}",
                    CountTables(model), migrationName);
                    
                return new SchemaOperationResult
                {
                    Success = true,
                    TablesAffected = CountTables(model),
                    MigrationName = migrationName,
                    GeneratedDdl = ddl,
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }
            else
            {
                // Log the DDL for debugging when execution fails
                _logger.LogError("Schema initialization failed: {Error}", result.Error);
                _logger.LogDebug("Failed DDL (first 500 chars): {Ddl}", ddl?.Length > 500 ? ddl[..500] + "..." : ddl);
                return SchemaOperationResult.Fail($"Schema initialization failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema initialization failed");
            return SchemaOperationResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<SchemaOperationResult> MigrateSchemaAsync(
        BmModel model,
        string? migrationName = null,
        bool safeMode = false,
        bool dryRun = false,
        bool force = false,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Step 1: Read current schema from database
            var schemaReader = new PostgresSchemaReader(_options.ConnectionString);
            var currentSchema = await schemaReader.ReadSchemaAsync();

            if (currentSchema.Tables.Count == 0)
            {
                return SchemaOperationResult.Fail("No existing schema found. Use InitializeSchemaAsync instead.");
            }

            // Step 2: Generate desired schema from model
            var ddlGenerator = new PostgresDdlGenerator(model);
            var desiredSchema = GenerateSchemaSnapshot(model);

            // Step 3: Compare schemas
            var schemaDiffer = new SchemaDiffer();
            var diff = schemaDiffer.Compare(desiredSchema, currentSchema);

            // Check if any changes detected
            if (!diff.HasChanges)
            {
                sw.Stop();
                return SchemaOperationResult.Ok(0, null, "No schema changes detected");
            }

            _logger.LogInformation("Detected changes: Tables to add={Add}, drop={Drop}, modify={Modify}",
                diff.TablesToAdd.Count, diff.TablesToDrop.Count, diff.TablesToModify.Count);

            // Step 4: Generate migration script
            var scriptGenerator = new MigrationScriptGenerator();
            var name = migrationName ?? $"Migration_{DateTime.UtcNow:yyyyMMddHHmmss}";
            
            Migration migration;
            if (safeMode)
            {
                migration = scriptGenerator.GenerateSafeMigration(diff, name);
                _logger.LogInformation("Safe mode enabled - backup tables will be created");
            }
            else
            {
                migration = scriptGenerator.GenerateMigration(diff, name);
            }

            // Step 5: Preview if dry-run
            if (dryRun)
            {
                sw.Stop();
                return new SchemaOperationResult
                {
                    Success = true,
                    TablesAffected = diff.TablesToAdd.Count + diff.TablesToDrop.Count + diff.TablesToModify.Count,
                    MigrationName = name,
                    GeneratedDdl = migration.UpScript,
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }

            // Step 6: Check for destructive operations without force
            if (diff.TablesToDrop.Count > 0 && !force)
            {
                return SchemaOperationResult.Fail(
                    $"Migration would DROP {diff.TablesToDrop.Count} table(s). Use force=true to proceed.");
            }

            // Step 7: Execute migration
            _logger.LogInformation("Applying migration '{Name}'...", name);
            var migrationExecutor = new MigrationExecutor(_options.ConnectionString);
            var result = await migrationExecutor.ApplyMigrationAsync(migration);

            sw.Stop();
            
            if (result.Success)
            {
                _logger.LogInformation("Migration '{Name}' applied successfully", name);
                return new SchemaOperationResult
                {
                    Success = true,
                    TablesAffected = diff.TablesToAdd.Count + diff.TablesToDrop.Count + diff.TablesToModify.Count,
                    MigrationName = name,
                    GeneratedDdl = migration.UpScript,
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }
            else
            {
                return SchemaOperationResult.Fail($"Migration failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema migration failed");
            return SchemaOperationResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<SchemaOperationResult> RollbackSchemaAsync(
        string? migrationName = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            var migrationExecutor = new MigrationExecutor(_options.ConnectionString);
            
            // Get migration to rollback
            string targetMigration;
            if (string.IsNullOrEmpty(migrationName))
            {
                var migrations = await migrationExecutor.GetAppliedMigrationsAsync();
                if (migrations.Count == 0)
                {
                    return SchemaOperationResult.Ok(0, null, "No migrations to rollback");
                }
                targetMigration = migrations[0].Name; // Most recent
            }
            else
            {
                targetMigration = migrationName;
            }

            _logger.LogInformation("Rolling back migration: {Name}", targetMigration);
            var result = await migrationExecutor.RollbackMigrationAsync(targetMigration);

            sw.Stop();
            
            if (result.Success)
            {
                _logger.LogInformation("Migration '{Name}' rolled back successfully", targetMigration);
                return new SchemaOperationResult
                {
                    Success = true,
                    MigrationName = targetMigration,
                    ExecutionTimeMs = sw.ElapsedMilliseconds
                };
            }
            else
            {
                return SchemaOperationResult.Fail($"Rollback failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema rollback failed");
            return SchemaOperationResult.Fail(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MigrationInfo>> GetMigrationHistoryAsync(CancellationToken ct = default)
    {
        var migrationExecutor = new MigrationExecutor(_options.ConnectionString);
        var migrations = await migrationExecutor.GetAppliedMigrationsAsync();
        
        return migrations.Select(m => new MigrationInfo
        {
            Name = m.Name,
            AppliedAt = m.Timestamp,
            Checksum = m.Checksum,
            UpScript = m.UpScript,
            DownScript = m.DownScript
        }).ToList();
    }

    /// <inheritdoc />
    public string GenerateDdlPreview(BmModel model)
    {
        var ddlGenerator = new PostgresDdlGenerator(model);
        return ddlGenerator.GenerateFullSchema();
    }

    /// <inheritdoc />
    public async Task<string> GenerateMigrationPreviewAsync(BmModel model, CancellationToken ct = default)
    {
        var schemaReader = new PostgresSchemaReader(_options.ConnectionString);
        var currentSchema = await schemaReader.ReadSchemaAsync();

        if (currentSchema.Tables.Count == 0)
            return "-- No existing schema to migrate from";

        var desiredSchema = GenerateSchemaSnapshot(model);
        var schemaDiffer = new SchemaDiffer();
        var diff = schemaDiffer.Compare(desiredSchema, currentSchema);

        if (!diff.HasChanges)
            return "-- No schema changes detected";

        var scriptGenerator = new MigrationScriptGenerator();
        var migration = scriptGenerator.GenerateMigration(diff, "Preview");
        return migration.UpScript;
    }

    #region Helper Methods

    private static string GenerateDropScript(BmModel model)
    {
        var sb = new StringBuilder();
        foreach (var entity in model.Entities)
        {
            var tableName = NamingConvention.ToSnakeCase(entity.Name);
            sb.AppendLine($"DROP TABLE IF EXISTS {tableName} CASCADE;");
        }
        return sb.ToString();
    }

    private static string GenerateDropAllScript(SchemaSnapshot schema)
    {
        // Registry tables that must NOT be dropped
        var registryTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core registry
            "modules", "tenants", "entities", "types", "enums", "services",
            "aspects", "views", "rules", "events", "sequences", "access_controls",
            "namespaces", "model_elements", "model_packages", "source_files",
            
            // Entity-related
            "entity_fields", "entity_associations", "entity_indexes", "entity_constraints",
            "entity_aspect_refs", "entity_constraint_fields", "entity_index_fields",
            "fields", "associations",
            
            // Type-related
            "type_fields", "enum_values", "type_definitions_legacy",
            
            // Service-related
            "service_operations", "operation_parameters", "service_exposed_entities",
            
            // Aspect-related
            "aspect_fields", "aspect_includes",
            
            // Module-related
            "module_installations", "module_dependencies", "module_deprecations",
            "package_dependencies",
            
            // Access control-related
            "access_rules", "access_field_restrictions", "access_rule_operations",
            "access_rule_principals",
            
            // Rule-related  
            "rule_statements", "rule_triggers", "rule_trigger_fields",
            
            // View-related
            "view_parameters",
            
            // Event-related
            "event_fields",
            
            // Misc
            "expression_nodes", "element_references", "annotations", "annotations_legacy",
            "migrations"
        };
        
        var sb = new StringBuilder();
        foreach (var table in schema.Tables)
        {
            // Skip migration history tables
            if (table.Name.StartsWith("__bmmdl"))
                continue;
                
            // CRITICAL: Skip ALL registry tables to prevent data loss!
            if (registryTables.Contains(table.Name))
                continue;
            
            sb.AppendLine($"DROP TABLE IF EXISTS {table.Name} CASCADE;");
        }
        return sb.ToString();
    }

    private static int CountTables(BmModel model) => model.Entities.Count;

    private static string ComputeChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLower()[..16];
    }



    private static SchemaSnapshot GenerateSchemaSnapshot(BmModel model)
    {
        var snapshot = new SchemaSnapshot();
        
        foreach (var entity in model.Entities)
        {
            var tableName = NamingConvention.ToSnakeCase(entity.Name);
            var table = new TableInfo { Name = tableName };
            
            foreach (var field in entity.Fields)
            {
                var column = new ColumnInfo
                {
                    Name = NamingConvention.ToSnakeCase(field.Name),
                    DataType = SqlTypeMapper.MapToSqlType(field.TypeRef?.ToTypeString() ?? field.TypeString ?? ""),
                    IsNullable = field.IsNullable
                };
                table.Columns.Add(column);
            }
            
            snapshot.Tables.Add(table);
        }
        
        return snapshot;
    }

    #endregion
}
