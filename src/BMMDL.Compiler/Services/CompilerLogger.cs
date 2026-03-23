using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Services;

/// <summary>
/// Factory for creating configured loggers throughout the compiler.
/// Supports both console output (CLI) and structured JSON (Kubernetes).
/// </summary>
public static class CompilerLoggerFactory
{
    private static ILoggerFactory? _factory;
    private static LogLevel _minLevel = LogLevel.Information;
    private static bool _useJson = false;

    /// <summary>
    /// Initialize the logger factory with specified settings.
    /// </summary>
    public static void Initialize(bool verbose = false, bool json = false)
    {
        _minLevel = verbose ? LogLevel.Debug : LogLevel.Information;
        _useJson = json;

        _factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(_minLevel);
            
            if (_useJson)
            {
                builder.AddJsonConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";
                });
            }
            else
            {
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = false;
                    options.IncludeScopes = false;
                    options.TimestampFormat = null; // No timestamp for CLI
                });
            }
        });
    }

    /// <summary>
    /// Get a logger for the specified type.
    /// </summary>
    public static ILogger<T> CreateLogger<T>()
    {
        EnsureInitialized();
        return _factory!.CreateLogger<T>();
    }

    /// <summary>
    /// Get a logger with the specified category name.
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
    {
        EnsureInitialized();
        return _factory!.CreateLogger(categoryName);
    }

    private static void EnsureInitialized()
    {
        if (_factory == null)
        {
            Initialize();
        }
    }

    /// <summary>
    /// Dispose the logger factory.
    /// </summary>
    public static void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }
}

/// <summary>
/// Extension methods for ILogger to provide BMMDL-specific logging patterns.
/// </summary>
public static class CompilerLoggerExtensions
{
    // Compilation events
    public static void LogPassStart(this ILogger logger, int passNumber, string passName)
        => logger.LogInformation("Pass {PassNumber}: {PassName}", passNumber, passName);

    public static void LogPassComplete(this ILogger logger, int passNumber, string passName, int itemCount, long elapsedMs)
        => logger.LogInformation("  ✓ {PassName} completed: {ItemCount} items ({ElapsedMs}ms)", passName, itemCount, elapsedMs);

    public static void LogPassFailed(this ILogger logger, int passNumber, string passName, string error)
        => logger.LogError("  ✗ {PassName} failed: {Error}", passName, error);

    // Persistence events
    public static void LogDbConnect(this ILogger logger, string host, string database)
        => logger.LogInformation("📦 Connecting to {Host}/{Database}...", host, database);

    public static void LogDbSaving(this ILogger logger, string entityType, int count)
        => logger.LogDebug("  Saving {Count} {EntityType}...", count, entityType);

    public static void LogDbSaved(this ILogger logger, string moduleName, string version)
        => logger.LogInformation("✅ Published: {ModuleName} v{Version}", moduleName, version);

    // Parse warnings (for empty catch replacements)
    public static void LogParseWarning(this ILogger logger, string file, int line, string message)
        => logger.LogWarning("[{File}:{Line}] {Message}", Path.GetFileName(file ?? "unknown"), line, message);
}
