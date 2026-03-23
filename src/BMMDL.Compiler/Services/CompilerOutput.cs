using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Services;

/// <summary>
/// Abstraction for compiler console output.
/// Provides both human-readable CLI output and structured logging.
/// </summary>
public interface ICompilerOutput
{
    void WriteLine(string message = "");
    void Write(string message);
    void WriteColored(string message, ConsoleColor color);
    void WriteSuccess(string message);
    void WriteError(string message);
    void WriteWarning(string message);
    void WriteInfo(string message);
    void WriteSeparator(int length = 50);
}

/// <summary>
/// Console-based output for CLI usage with optional colors.
/// </summary>
public class ConsoleCompilerOutput : ICompilerOutput
{
    private readonly bool _useColors;
    private readonly ILogger? _logger;

    public ConsoleCompilerOutput(bool useColors = true, ILogger? logger = null)
    {
        _useColors = useColors;
        _logger = logger;
    }

    public void WriteLine(string message = "")
    {
        Console.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(message))
            _logger?.LogDebug("{Message}", message);
    }

    public void Write(string message)
    {
        Console.Write(message);
    }

    public void WriteColored(string message, ConsoleColor color)
    {
        if (_useColors)
        {
            Console.ForegroundColor = color;
            Console.Write(message);
            Console.ResetColor();
        }
        else
        {
            Console.Write(message);
        }
    }

    public void WriteSuccess(string message)
    {
        WriteColored("✅ ", ConsoleColor.Green);
        WriteLine(message);
        _logger?.LogInformation("{Message}", message);
    }

    public void WriteError(string message)
    {
        WriteColored("❌ ", ConsoleColor.Red);
        WriteLine(message);
        _logger?.LogError("{Message}", message);
    }

    public void WriteWarning(string message)
    {
        WriteColored("⚠️ ", ConsoleColor.Yellow);
        WriteLine(message);
        _logger?.LogWarning("{Message}", message);
    }

    public void WriteInfo(string message)
    {
        WriteColored("ℹ️ ", ConsoleColor.Cyan);
        WriteLine(message);
        _logger?.LogInformation("{Message}", message);
    }

    public void WriteSeparator(int length = 50)
    {
        WriteLine(new string('─', length));
    }
}

/// <summary>
/// Silent output that only logs - no console output.
/// Useful for Kubernetes/daemon mode.
/// </summary>
public class LogOnlyCompilerOutput : ICompilerOutput
{
    private readonly ILogger _logger;

    public LogOnlyCompilerOutput(ILogger logger)
    {
        _logger = logger;
    }

    public void WriteLine(string message = "")
    {
        if (!string.IsNullOrWhiteSpace(message))
            _logger.LogInformation("{Message}", message);
    }

    public void Write(string message)
    {
        // No console output in log-only mode
    }

    public void WriteColored(string message, ConsoleColor color)
    {
        // No console output in log-only mode
    }

    public void WriteSuccess(string message) => _logger.LogInformation("SUCCESS: {Message}", message);
    public void WriteError(string message) => _logger.LogError("ERROR: {Message}", message);
    public void WriteWarning(string message) => _logger.LogWarning("WARNING: {Message}", message);
    public void WriteInfo(string message) => _logger.LogInformation("INFO: {Message}", message);
    public void WriteSeparator(int length = 50) { }
}
