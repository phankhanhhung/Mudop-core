using Microsoft.Extensions.Logging;

namespace BMMDL.SchemaManager;

/// <summary>
/// Configuration options for schema manager.
/// </summary>
public class SchemaManagerOptions
{
    /// <summary>
    /// Database connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "";
    
    /// <summary>
    /// Enable verbose output/logging.
    /// </summary>
    public bool Verbose { get; set; } = false;
    
    /// <summary>
    /// Optional logger for structured logging.
    /// </summary>
    public ILogger? Logger { get; set; }
    
    /// <summary>
    /// Validate required options.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException("ConnectionString is required", nameof(ConnectionString));
    }
}
