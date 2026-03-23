using BMMDL.CodeGen;
using BMMDL.CodeGen.Schema;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Data;
using BMMDL.Registry.Services;
using BMMDL.SchemaManager;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace BMMDL.Registry.Api.Services;

/// <summary>
/// Manages database schema operations: creation, migration, and teardown.
/// </summary>
public class SchemaManagementService : ISchemaManagementService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SchemaManagementService> _logger;
    private readonly RegistryDbContext _registryDb;

    public SchemaManagementService(
        IConfiguration configuration,
        ILogger<SchemaManagementService> logger,
        RegistryDbContext registryDb)
    {
        _configuration = configuration;
        _logger = logger;
        _registryDb = registryDb;
    }

    /// <summary>
    /// Get the database connection string from configuration or environment variables.
    /// </summary>
    public string GetConnectionString()
    {
        return _configuration.GetConnectionString("DefaultConnection")
            ?? $"Host={Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost"};" +
               $"Port={Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"};" +
               $"Database={Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bmmdl_registry"};" +
               $"Username={Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "bmmdl"};" +
               $"Password={Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "bmmdl"}";
    }

    /// <summary>
    /// Initialize schema from scratch (fresh CREATE TABLE).
    /// </summary>
    public async Task<string> InitSchemaFreshAsync(BmModel schemaModel, string connString)
    {
        var schemaOptions = new SchemaManagerOptions
        {
            ConnectionString = connString,
            Verbose = false
        };

        var moduleSchemaName = NamingConvention.GetSchemaName(schemaModel.Module?.Name ?? schemaModel.Namespace ?? "platform");

        // Wrap schema initialization in a transaction so partial failures are rolled back
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var schemaManager = new PostgresSchemaManager(schemaOptions);
            var schemaOpResult = await schemaManager.InitializeSchemaAsync(
                schemaModel, force: true, dryRun: false);

            if (schemaOpResult.Success)
            {
                await tx.CommitAsync();
                _logger.LogInformation("Schema initialized: {TablesAffected} tables", schemaOpResult.TablesAffected);
                return $"Created {schemaOpResult.TablesAffected} tables";
            }

            // Schema init reported failure — roll back
            await tx.RollbackAsync();
            _logger.LogWarning("Schema init failed, rolled back: {Error}", schemaOpResult.Error);
            return $"Schema init failed (rolled back): {schemaOpResult.Error}";
        }
        catch (Exception ex)
        {
            // Exception during schema init — roll back to avoid partial state
            try
            {
                await tx.RollbackAsync();
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Rollback also failed after schema init error");
            }

            _logger.LogError(ex, "Schema initialization failed for {Schema}, rolled back", moduleSchemaName);
            return $"Schema init failed (rolled back): {ex.Message}";
        }
    }

    /// <summary>
    /// Migrate schema using ALTER TABLE instead of DROP+CREATE.
    /// Reads current schema, compares with target, generates and executes migration SQL.
    /// Falls back to DROP+CREATE if migration fails or schema doesn't exist.
    /// </summary>
    public async Task<string> MigrateSchemaAsync(
        BmModel schemaModel, string connString, string schemaName, List<string> warnings)
    {
        try
        {
            // Check if schema exists first
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            var checkSql = "SELECT EXISTS(SELECT 1 FROM information_schema.schemata WHERE schema_name = @schema)";
            await using var checkCmd = new NpgsqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("@schema", schemaName);
            var schemaExists = (bool)(await checkCmd.ExecuteScalarAsync() ?? false);

            if (!schemaExists)
            {
                // Schema doesn't exist — create fresh
                _logger.LogInformation("Schema {Schema} doesn't exist, creating fresh", schemaName);
                var safeSchema = NamingConvention.QuoteIdentifier(schemaName);
                await using var createCmd = new NpgsqlCommand($"CREATE SCHEMA {safeSchema}", conn);
                await createCmd.ExecuteNonQueryAsync();
                await conn.CloseAsync();

                return await InitSchemaFreshAsync(schemaModel, connString);
            }

            await conn.CloseAsync();

            // Read current schema from database
            var schemaReader = new SchemaReader();
            var currentSnapshot = await schemaReader.ReadSchemaAsync(connString, schemaName);

            if (currentSnapshot.Tables.Count == 0)
            {
                // Schema exists but has no tables — create fresh
                _logger.LogInformation("Schema {Schema} exists but is empty, initializing fresh", schemaName);
                return await InitSchemaFreshAsync(schemaModel, connString);
            }

            // Build target schema from compiled model — populate a Registry MetaModelCache for TypeResolver
            var typeCache = new MetaModelCache();
            foreach (var entity in schemaModel.Entities) typeCache.AddEntity(entity);
            foreach (var type in schemaModel.Types) typeCache.AddType(type);
            foreach (var e in schemaModel.Enums) typeCache.AddEnum(e);
            foreach (var aspect in schemaModel.Aspects) typeCache.AddAspect(aspect);
            var resolver = new TypeResolver(typeCache);
            var comparer = new SchemaComparer();
            var targetSnapshot = comparer.BuildTargetSnapshot(schemaModel, schemaName, resolver);

            // Compare
            var diff = comparer.Compare(currentSnapshot, targetSnapshot);

            if (!diff.HasChanges)
            {
                _logger.LogInformation("No schema changes detected for {Schema}", schemaName);
                return "Schema up to date (no changes)";
            }

            // Generate migration
            var migrationGenerator = new MigrationScriptGenerator();
            var migration = migrationGenerator.GenerateSafeMigration(
                diff, $"auto_{DateTime.UtcNow:yyyyMMdd_HHmmss}");

            _logger.LogInformation(
                "Schema migration for {Schema}: {Added} tables to add, {Modified} to modify, {Dropped} to drop",
                schemaName, diff.TablesToAdd.Count, diff.TablesToModify.Count, diff.TablesToDrop.Count);

            // Execute migration within transaction
            await using var migConn = new NpgsqlConnection(connString);
            await migConn.OpenAsync();
            await using var tx = await migConn.BeginTransactionAsync();

            try
            {
                await using var migCmd = new NpgsqlCommand(migration.UpScript, migConn, tx);
                migCmd.CommandTimeout = 120;
                await migCmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();

                var result = $"Migrated: {diff.TablesToAdd.Count} added, " +
                             $"{diff.TablesToModify.Count} modified, " +
                             $"{diff.TablesToDrop.Count} dropped";
                _logger.LogInformation("Schema migration successful: {Result}", result);
                return result;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogWarning(ex, "Schema migration failed, falling back to DROP+CREATE");
                warnings.Add($"Migration failed ({ex.Message}), fell back to DROP+CREATE");

                // Fall back to DROP+CREATE
                await DropSchemaIfExistsAsync(connString, schemaName);
                return await InitSchemaFreshAsync(schemaModel, connString);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Schema migration error, falling back to DROP+CREATE");
            warnings.Add($"Migration error ({ex.Message}), fell back to DROP+CREATE");

            await DropSchemaIfExistsAsync(connString, schemaName);
            return await InitSchemaFreshAsync(schemaModel, connString);
        }
    }

    /// <summary>
    /// Drop a specific schema and all its tables (CASCADE), then recreate the empty schema.
    /// Used before InitSchema to ensure clean slate for the module's schema.
    /// </summary>
    public async Task DropSchemaIfExistsAsync(string connString, string schemaName)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // Drop and recreate the schema to ensure a clean slate (escape double quotes to prevent injection)
            var safeSchemaName = NamingConvention.QuoteIdentifier(schemaName);
            await using var dropCmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {safeSchemaName} CASCADE", conn);
            await dropCmd.ExecuteNonQueryAsync();

            await using var createCmd = new NpgsqlCommand($"CREATE SCHEMA {safeSchemaName}", conn);
            await createCmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Recreated schema {SchemaName} for clean initialization", schemaName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recreate schema {SchemaName}, continuing with init", schemaName);
        }
    }

    /// <summary>
    /// Ensure registry schema and tables exist. Creates them if missing.
    /// </summary>
    /// <returns>True if schema was newly created, false if it already existed</returns>
    public async Task<bool> EnsureRegistrySchemaExistsAsync(List<string> messages)
    {
        try
        {
            // Check if registry schema exists
            var connString = GetConnectionString();
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            var checkSql = "SELECT EXISTS(SELECT 1 FROM information_schema.schemata WHERE schema_name = 'registry');";
            await using var checkCmd = new NpgsqlCommand(checkSql, conn);
            var exists = (bool)(await checkCmd.ExecuteScalarAsync() ?? false);

            if (!exists)
            {
                _logger.LogInformation("Registry schema does not exist. Creating...");
                messages.Add("Registry schema not found - creating...");

                // Step 1: Create the schema via raw SQL (EF Core doesn't auto-create schemas)
                await using var createSchemaCmd = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS registry;", conn);
                await createSchemaCmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Created 'registry' schema");

                // Close connection before EF Core uses its own
                await conn.CloseAsync();

                // Step 2: Force table creation via DatabaseCreator
                var databaseCreator = _registryDb.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
                await databaseCreator.CreateTablesAsync();

                _logger.LogInformation("Registry schema and tables created successfully");
                messages.Add("Registry schema created successfully");
                return true;  // Schema was created
            }

            return false;  // Schema already existed
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure registry schema exists");
            messages.Add($"Warning: Could not verify registry schema: {ex.Message}");
            return false;
        }
    }
}
