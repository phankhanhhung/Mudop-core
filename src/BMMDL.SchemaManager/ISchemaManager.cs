using BMMDL.MetaModel;

namespace BMMDL.SchemaManager;

/// <summary>
/// Interface for schema management operations.
/// Accepts BmModel reference from host process.
/// </summary>
public interface ISchemaManager
{
    /// <summary>
    /// Initialize database schema from BmModel (CREATE tables).
    /// </summary>
    /// <param name="model">The meta-model to generate schema from.</param>
    /// <param name="force">If true, drops existing tables before creating.</param>
    /// <param name="dryRun">If true, generates DDL but does not execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result with status and details.</returns>
    Task<SchemaOperationResult> InitializeSchemaAsync(
        BmModel model,
        bool force = false,
        bool dryRun = false,
        CancellationToken ct = default);
    
    /// <summary>
    /// Migrate database schema incrementally (ALTER tables).
    /// Compares current DB schema with model and generates migration.
    /// </summary>
    /// <param name="model">The target meta-model state.</param>
    /// <param name="migrationName">Optional name for the migration.</param>
    /// <param name="safeMode">If true, creates backup tables for destructive operations.</param>
    /// <param name="dryRun">If true, generates migration script but does not execute.</param>
    /// <param name="force">If true, skips confirmation for destructive operations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result with status and details.</returns>
    Task<SchemaOperationResult> MigrateSchemaAsync(
        BmModel model,
        string? migrationName = null,
        bool safeMode = false,
        bool dryRun = false,
        bool force = false,
        CancellationToken ct = default);
    
    /// <summary>
    /// Rollback a migration.
    /// </summary>
    /// <param name="migrationName">Name of migration to rollback. If null, rolls back the last migration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result with status and details.</returns>
    Task<SchemaOperationResult> RollbackSchemaAsync(
        string? migrationName = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get list of applied migrations.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of migration info ordered by applied date descending.</returns>
    Task<IReadOnlyList<MigrationInfo>> GetMigrationHistoryAsync(
        CancellationToken ct = default);
    
    /// <summary>
    /// Generate DDL preview without executing.
    /// Useful for reviewing what will be created.
    /// </summary>
    /// <param name="model">The meta-model to generate DDL from.</param>
    /// <returns>DDL string.</returns>
    string GenerateDdlPreview(BmModel model);
    
    /// <summary>
    /// Generate migration script preview without executing.
    /// Compares current DB state with model.
    /// </summary>
    /// <param name="model">The target meta-model state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Migration script string.</returns>
    Task<string> GenerateMigrationPreviewAsync(BmModel model, CancellationToken ct = default);
}
