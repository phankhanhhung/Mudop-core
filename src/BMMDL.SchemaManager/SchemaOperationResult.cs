namespace BMMDL.SchemaManager;

/// <summary>
/// Result of a schema operation.
/// </summary>
public class SchemaOperationResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// Name of the migration applied (if any).
    /// </summary>
    public string? MigrationName { get; set; }
    
    /// <summary>
    /// Number of tables affected by the operation.
    /// </summary>
    public int TablesAffected { get; set; }
    
    /// <summary>
    /// Generated DDL (for dry-run or preview).
    /// </summary>
    public string? GeneratedDdl { get; set; }
    
    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    public long ExecutionTimeMs { get; set; }
    
    /// <summary>
    /// Create a success result.
    /// </summary>
    public static SchemaOperationResult Ok(int tablesAffected = 0, string? migrationName = null, string? ddl = null)
        => new()
        {
            Success = true,
            TablesAffected = tablesAffected,
            MigrationName = migrationName,
            GeneratedDdl = ddl
        };
    
    /// <summary>
    /// Create a failure result.
    /// </summary>
    public static SchemaOperationResult Fail(string error)
        => new()
        {
            Success = false,
            Error = error
        };
}
