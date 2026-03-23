using BMMDL.MetaModel;
using BMMDL.SchemaManager;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Services;

/// <summary>
/// CLI adapter for SchemaManager. Provides CLI-friendly output formatting
/// and delegates actual work to ISchemaManager.
/// </summary>
/// <remarks>
/// This class is kept for backward compatibility with CLI commands.
/// For new integrations, use ISchemaManager directly.
/// </remarks>
public class SchemaInitializationService
{
    private readonly ICompilerOutput _output;
    private readonly bool _verbose;

    public SchemaInitializationService(bool verbose, ICompilerOutput output)
    {
        _verbose = verbose;
        _output = output;
    }

    /// <summary>
    /// Initialize business domain schema from scratch (CREATE tables).
    /// </summary>
    public async Task<bool> InitializeSchemaAsync(
        BmModel model,
        string connectionString,
        bool force = false,
        bool dryRun = false)
    {
        _output.WriteLine("📦 Initializing business domain schema...");

        var options = new SchemaManagerOptions
        {
            ConnectionString = connectionString,
            Verbose = _verbose,
            Logger = CompilerLoggerFactory.CreateLogger("SchemaInit")
        };
        
        var schemaManager = new PostgresSchemaManager(options);
        var result = await schemaManager.InitializeSchemaAsync(model, force, dryRun);

        if (result.Success)
        {
            if (dryRun)
            {
                _output.WriteLine("\n=== DRY RUN: DDL PREVIEW ===\n");
                _output.WriteLine(result.GeneratedDdl ?? "");
                _output.WriteLine("\n=== END PREVIEW ===\n");
                _output.WriteSuccess($"[DRY RUN] Would create {result.TablesAffected} table(s)");
            }
            else
            {
                _output.WriteSuccess("✅ Schema initialized successfully");
                _output.WriteLine($"   Tables: {result.TablesAffected}");
                _output.WriteLine($"   Migration: {result.MigrationName}");
            }
            return true;
        }
        else
        {
            _output.WriteError($"❌ Schema initialization failed: {result.Error}");
            return false;
        }
    }

    /// <summary>
    /// Migrate business domain schema incrementally (ALTER tables).
    /// </summary>
    public async Task<bool> MigrateSchemaAsync(
        BmModel model,
        string connectionString,
        string? migrationName = null,
        bool safeMode = false,
        bool dryRun = false,
        bool force = false)
    {
        _output.WriteLine("🔄 Migrating business domain schema...");

        var options = new SchemaManagerOptions
        {
            ConnectionString = connectionString,
            Verbose = _verbose,
            Logger = CompilerLoggerFactory.CreateLogger("SchemaMigrate")
        };
        
        var schemaManager = new PostgresSchemaManager(options);
        var result = await schemaManager.MigrateSchemaAsync(model, migrationName, safeMode, dryRun, force);

        if (result.Success)
        {
            if (dryRun)
            {
                _output.WriteLine("\n=== DRY RUN: MIGRATION PREVIEW ===\n");
                _output.WriteLine(result.GeneratedDdl ?? "No schema changes detected");
                _output.WriteLine("\n=== END PREVIEW ===\n");
                _output.WriteSuccess($"[DRY RUN] Migration '{result.MigrationName}' generated but not applied");
            }
            else if (result.MigrationName != null)
            {
                _output.WriteSuccess($"✅ Migration '{result.MigrationName}' applied successfully");
            }
            else
            {
                _output.WriteSuccess("No schema changes detected.");
            }
            return true;
        }
        else
        {
            _output.WriteError($"❌ Migration failed: {result.Error}");
            return false;
        }
    }

    /// <summary>
    /// Rollback the last migration or a specific migration.
    /// </summary>
    public async Task<bool> RollbackSchemaAsync(
        string connectionString,
        string? migrationName = null)
    {
        _output.WriteLine("↩️  Rolling back schema...");

        var options = new SchemaManagerOptions
        {
            ConnectionString = connectionString,
            Verbose = _verbose,
            Logger = CompilerLoggerFactory.CreateLogger("SchemaRollback")
        };
        
        var schemaManager = new PostgresSchemaManager(options);
        var result = await schemaManager.RollbackSchemaAsync(migrationName);

        if (result.Success)
        {
            if (result.MigrationName != null)
            {
                _output.WriteSuccess($"✅ Migration '{result.MigrationName}' rolled back successfully");
            }
            else
            {
                _output.WriteSuccess("No migrations to rollback.");
            }
            return true;
        }
        else
        {
            _output.WriteError($"❌ Rollback failed: {result.Error}");
            return false;
        }
    }

    /// <summary>
    /// Get migration history from the database.
    /// </summary>
    public async Task<IReadOnlyList<MigrationInfo>> GetMigrationHistoryAsync(string connectionString)
    {
        var options = new SchemaManagerOptions
        {
            ConnectionString = connectionString,
            Verbose = _verbose
        };
        
        var schemaManager = new PostgresSchemaManager(options);
        return await schemaManager.GetMigrationHistoryAsync();
    }
}
