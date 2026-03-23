using BMMDL.Compiler.Services;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Shared utilities for CLI command handlers.
/// </summary>
internal static class CommandHelper
{
    /// <summary>
    /// Builds a PostgreSQL connection string from POSTGRES_* environment variables.
    /// Returns null if required variables (POSTGRES_DATABASE, POSTGRES_PASSWORD) are missing.
    /// </summary>
    public static string? GetConnectionStringFromEnv()
    {
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DATABASE");
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

        if (string.IsNullOrEmpty(database) || string.IsNullOrEmpty(password))
            return null;

        return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
    }

    /// <summary>
    /// Resolves the effective connection string from explicit parameter or environment.
    /// Writes an error and returns null if neither is available.
    /// </summary>
    public static string? ResolveConnectionString(string? explicit_, ConsoleCompilerOutput output)
    {
        var connString = explicit_ ?? GetConnectionStringFromEnv();
        if (string.IsNullOrEmpty(connString))
        {
            output.WriteError("No connection string provided. Use -c or set POSTGRES_* environment variables.");
        }
        return connString;
    }

    /// <summary>
    /// Resolves file paths, optionally including auto-resolved dependencies.
    /// Returns null on failure (error already written to output).
    /// </summary>
    public static List<string>? ResolveFilePaths(
        FileInfo[] files, bool resolveDeps, string? modulesDir, bool verbose,
        ConsoleCompilerOutput output, bool printTree = false)
    {
        var filePaths = files.Select(f => f.FullName).ToList();

        if (resolveDeps && files.Length == 1)
        {
            var resolver = new ModuleDependencyResolver(verbose, output);

            if (printTree)
                resolver.PrintDependencyTree(filePaths[0], modulesDir);

            try
            {
                filePaths = resolver.ResolveDependencies(filePaths[0], modulesDir);
            }
            catch (Exception ex)
            {
                output.WriteError($"Dependency resolution failed: {ex.Message}");
                return null;
            }
        }
        else if (resolveDeps && files.Length > 1)
        {
            output.WriteWarning("--resolve-deps only works with a single target module. Compiling all specified files instead.");
        }

        return filePaths;
    }
}
