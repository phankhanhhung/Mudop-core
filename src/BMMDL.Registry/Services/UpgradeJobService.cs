using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Registry.Data;
using BMMDL.Registry.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BMMDL.Registry.Services;

/// <summary>
/// Background job service for upgrade data migration.
/// Handles bulk migration and delta sync activation.
/// </summary>
public class UpgradeJobService
{
    private readonly RegistryDbContext _db;
    private readonly DualVersionSyncService _syncService;
    private readonly SyncTriggerGenerator _triggerGen;
    private readonly ILogger<UpgradeJobService> _logger;
    private readonly string _connectionString;

    public UpgradeJobService(
        RegistryDbContext db,
        DualVersionSyncService syncService,
        ILogger<UpgradeJobService> logger,
        string connectionString,
        string schemaName = "public")
    {
        _db = db;
        _syncService = syncService;
        _logger = logger;
        _connectionString = connectionString;
        _triggerGen = new SyncTriggerGenerator(schemaName);
    }

    /// <summary>
    /// Execute full upgrade workflow:
    /// 1. Create v2 tables
    /// 2. Bulk migrate data from v1 to v2
    /// 3. Create and activate sync triggers
    /// 4. Transition to DualVersion state
    /// </summary>
    public async Task ExecuteUpgradeAsync(
        Guid windowId,
        BmModel v1Model,
        BmModel v2Model,
        CancellationToken ct = default)
    {
        var window = await _db.UpgradeWindows.FindAsync(new object[] { windowId }, ct);
        if (window == null)
        {
            _logger.LogError("Upgrade window {WindowId} not found", windowId);
            return;
        }

        try
        {
            // Phase 1: Preparing
            await _syncService.TransitionStatusAsync(windowId, UpgradeStatus.Preparing, ct);
            _logger.LogInformation("Upgrade {WindowId}: Starting preparation phase", windowId);

            // Initialize sync statuses
            await _syncService.InitializeSyncStatusesAsync(windowId, v1Model, v2Model, ct);

            // Create v2 tables and bulk migrate
            foreach (var entity in v2Model.Entities)
            {
                await MigrateEntityAsync(windowId, entity, v1Model, ct);
            }

            // Phase 2: DualVersion with sync triggers
            await _syncService.TransitionStatusAsync(windowId, UpgradeStatus.DualVersion, ct);
            _logger.LogInformation("Upgrade {WindowId}: Dual-version mode activated", windowId);

            // Activate sync triggers for all entities
            foreach (var entity in v2Model.Entities)
            {
                await ActivateSyncTriggerAsync(windowId, entity, v1Model, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upgrade {WindowId} failed", windowId);
            await _syncService.TransitionStatusAsync(windowId, UpgradeStatus.Failed, ct);
            throw;
        }
    }

    /// <summary>
    /// Migrate a single entity: copy data from v1 to v2.
    /// </summary>
    private async Task MigrateEntityAsync(
        Guid windowId,
        BmEntity v2Entity,
        BmModel v1Model,
        CancellationToken ct)
    {
        var entityName = v2Entity.QualifiedName;
        var v1Entity = v1Model.Entities.FirstOrDefault(e => e.QualifiedName == entityName);

        await _syncService.UpdateSyncProgressAsync(
            windowId, entityName, 0, phase: SyncPhase.BulkMigration, ct: ct);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var v1Table = GetTableName(v1Entity ?? v2Entity, 1);
        var v2Table = GetTableName(v2Entity, 2);

        // Get total count
        var countSql = $"SELECT COUNT(*) FROM {v1Table}";
        await using var countCmd = new NpgsqlCommand(countSql, conn);
        var totalRecords = (long)(await countCmd.ExecuteScalarAsync(ct) ?? 0);

        await _syncService.UpdateSyncProgressAsync(
            windowId, entityName, 0, totalRecords: totalRecords, ct: ct);

        if (v1Entity == null)
        {
            // New entity in v2, no migration needed
            _logger.LogInformation("Entity {Entity} is new in v2, skipping migration", entityName);
            await _syncService.UpdateSyncProgressAsync(
                windowId, entityName, 0, phase: SyncPhase.Completed, ct: ct);
            return;
        }

        // Bulk copy with batching
        const int batchSize = 10000;
        long migratedRecords = 0;

        // Find common columns
        var v1Columns = v1Entity.Fields.Select(f => NamingConvention.ToSnakeCase(f.Name)).ToList();
        var v2Columns = v2Entity.Fields.Select(f => NamingConvention.ToSnakeCase(f.Name)).ToList();
        var commonColumns = v1Columns.Intersect(v2Columns).ToList();
        commonColumns.Insert(0, "id"); // Always include id

        var columnList = string.Join(", ", commonColumns.Select(NamingConvention.QuoteIdentifier));
        var quotedId = NamingConvention.QuoteIdentifier("id");

        while (migratedRecords < totalRecords)
        {
            var insertSql = $@"
                INSERT INTO {v2Table} ({columnList})
                SELECT {columnList} FROM {v1Table}
                ORDER BY {quotedId}
                OFFSET {migratedRecords} LIMIT {batchSize}
                ON CONFLICT ({quotedId}) DO NOTHING";

            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            var affected = await insertCmd.ExecuteNonQueryAsync(ct);
            
            migratedRecords += affected;

            await _syncService.UpdateSyncProgressAsync(
                windowId, entityName, migratedRecords, ct: ct);

            _logger.LogDebug("Entity {Entity}: Migrated {Count}/{Total}", 
                entityName, migratedRecords, totalRecords);
        }

        await _syncService.UpdateSyncProgressAsync(
            windowId, entityName, migratedRecords, phase: SyncPhase.Completed, ct: ct);

        _logger.LogInformation("Entity {Entity}: Bulk migration complete ({Count} records)", 
            entityName, migratedRecords);
    }

    /// <summary>
    /// Create and activate sync trigger for bidirectional sync.
    /// </summary>
    private async Task ActivateSyncTriggerAsync(
        Guid windowId,
        BmEntity v2Entity,
        BmModel v1Model,
        CancellationToken ct)
    {
        var entityName = v2Entity.QualifiedName;
        var v1Entity = v1Model.Entities.FirstOrDefault(e => e.QualifiedName == entityName);

        if (v1Entity == null)
        {
            _logger.LogDebug("Entity {Entity} is new, no sync trigger needed", entityName);
            return;
        }

        // Generate trigger SQL
        var triggerResult = _triggerGen.GenerateV2ToV1SyncTrigger(entityName, v1Entity, v2Entity);
        var createSql = triggerResult.GetCreateSql();

        if (string.IsNullOrEmpty(createSql))
        {
            _logger.LogWarning("No sync trigger generated for {Entity}", entityName);
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(createSql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        await _syncService.ActivateSyncTriggerAsync(windowId, entityName, ct);

        _logger.LogInformation("Entity {Entity}: Sync trigger activated", entityName);
    }

    /// <summary>
    /// Validate upgrade before completing.
    /// </summary>
    public async Task<ValidationResult> ValidateUpgradeAsync(
        Guid windowId,
        BmModel v2Model,
        CancellationToken ct = default)
    {
        var result = new ValidationResult { IsValid = true };

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var entity in v2Model.Entities)
        {
            var v2Table = GetTableName(entity, 2);
            var v1Table = GetTableName(entity, 1);

            // Compare counts
            var v1CountSql = $"SELECT COUNT(*) FROM {v1Table}";
            var v2CountSql = $"SELECT COUNT(*) FROM {v2Table}";

            try
            {
                await using var v1Cmd = new NpgsqlCommand(v1CountSql, conn);
                await using var v2Cmd = new NpgsqlCommand(v2CountSql, conn);

                var v1Count = (long)(await v1Cmd.ExecuteScalarAsync(ct) ?? 0);
                var v2Count = (long)(await v2Cmd.ExecuteScalarAsync(ct) ?? 0);

                if (v1Count != v2Count)
                {
                    result.IsValid = false;
                    result.Errors.Add($"{entity.QualifiedName}: Count mismatch (v1={v1Count}, v2={v2Count})");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{entity.QualifiedName}: Validation error - {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Clean up v1 tables after grace period.
    /// </summary>
    public async Task CleanupV1TablesAsync(
        Guid windowId,
        BmModel v1Model,
        CancellationToken ct = default)
    {
        var window = await _db.UpgradeWindows.FindAsync(new object[] { windowId }, ct);
        if (window == null || window.Status != UpgradeStatus.Completed)
        {
            _logger.LogWarning("Cannot cleanup: window not completed");
            return;
        }

        if (window.V1CleanupAfter > DateTime.UtcNow)
        {
            _logger.LogInformation("Grace period not expired, cleanup postponed until {Date}", 
                window.V1CleanupAfter);
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var entity in v1Model.Entities)
        {
            var v1Table = GetTableName(entity, 1);
            var dropSql = $"DROP TABLE IF EXISTS {v1Table} CASCADE";

            await using var cmd = new NpgsqlCommand(dropSql, conn);
            await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("Dropped v1 table: {Table}", v1Table);
        }
    }

    private static string GetTableName(BmEntity entity, int version)
    {
        var schema = NamingConvention.GetSchemaName(entity.Namespace);
        var table = NamingConvention.ToSnakeCase(entity.Name);
        return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier($"{table}_v{version}")}";
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
