using System.CommandLine;
using BMMDL.Compiler.Pipeline;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Migrates the business domain schema incrementally.
/// Usage: bmmdlc migrate-schema &lt;files&gt; [-c conn] [-r] [-m dir] [-n name] [--safe] [--dry-run] [--force]
/// </summary>
internal static class MigrateSchemaCommand
{
    public static Command Create()
    {
        var filesArg = new Argument<FileInfo[]>("files", "BMMDL module file(s)")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var connectionOption = new Option<string?>(
            aliases: new[] { "-c", "--connection" },
            description: "Database connection string");

        var resolveOption = new Option<bool>(
            aliases: new[] { "-r", "--resolve-deps" },
            description: "Auto-resolve and include module dependencies");

        var modulesDirOption = new Option<string?>(
            aliases: new[] { "-m", "--modules-dir" },
            description: "Base directory for module discovery");

        var nameOption = new Option<string?>(
            aliases: new[] { "-n", "--name" },
            description: "Migration name");

        var safeOption = new Option<bool>(
            aliases: new[] { "--safe" },
            description: "Safe mode (create backups before destructive operations)");

        var dryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run" },
            description: "Preview migration without executing");

        var forceOption = new Option<bool>(
            aliases: new[] { "--force" },
            description: "Skip confirmations for destructive operations");

        var command = new Command("migrate-schema", "Migrate business domain schema incrementally");
        command.AddArgument(filesArg);
        command.AddOption(connectionOption);
        command.AddOption(resolveOption);
        command.AddOption(modulesDirOption);
        command.AddOption(nameOption);
        command.AddOption(safeOption);
        command.AddOption(dryRunOption);
        command.AddOption(forceOption);

        // Note: Using 8 parameters max (System.CommandLine limit)
        command.SetHandler(async (files, connection, resolveDeps, modulesDir, name, safe, dryRun, force) =>
        {
            var output = new ConsoleCompilerOutput(useColors: true);

            var filePaths = CommandHelper.ResolveFilePaths(files, resolveDeps, modulesDir, verbose: false, output);
            if (filePaths == null)
            {
                Environment.ExitCode = 1;
                return;
            }

            // Compile the model first
            var options = new CompilationOptions { Verbose = false, ShowProgress = true, UseColors = true };
            var pipeline = new CompilerPipeline(options, output);
            var result = pipeline.Compile(filePaths);

            if (!result.Success || result.Context.Model == null)
            {
                output.WriteError("Compilation failed. Cannot migrate schema.");
                Environment.ExitCode = 1;
                return;
            }

            // Get connection string
            var connString = CommandHelper.ResolveConnectionString(connection, output);
            if (string.IsNullOrEmpty(connString))
            {
                Environment.ExitCode = 1;
                return;
            }

            // Migrate schema
            var schemaService = new SchemaInitializationService(verbose: false, output);
            var success = await schemaService.MigrateSchemaAsync(
                result.Context.Model, connString, name, safe, dryRun, force);

            Environment.ExitCode = success ? 0 : 1;
        }, filesArg, connectionOption, resolveOption, modulesDirOption, nameOption, safeOption, dryRunOption, forceOption);

        return command;
    }
}
