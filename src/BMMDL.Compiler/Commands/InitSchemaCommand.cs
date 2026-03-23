using System.CommandLine;
using BMMDL.Compiler.Pipeline;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Initializes the business domain schema from a compiled BMMDL model.
/// Usage: bmmdlc init-schema &lt;files&gt; [-c conn] [-r] [-m dir] [--force] [--dry-run] [-v] [--no-color]
/// </summary>
internal static class InitSchemaCommand
{
    public static Command Create()
    {
        var filesArg = new Argument<FileInfo[]>("files", "BMMDL module file(s)")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var connectionOption = new Option<string?>(
            aliases: new[] { "-c", "--connection" },
            description: "Database connection string (uses POSTGRES_* env vars if not specified)");

        var resolveOption = new Option<bool>(
            aliases: new[] { "-r", "--resolve-deps" },
            description: "Auto-resolve and include module dependencies");

        var modulesDirOption = new Option<string?>(
            aliases: new[] { "-m", "--modules-dir" },
            description: "Base directory for module discovery");

        var forceOption = new Option<bool>(
            aliases: new[] { "--force" },
            description: "Force recreation if schema exists (drops existing tables)");

        var dryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run" },
            description: "Preview DDL without executing");

        var verboseOption = new Option<bool>(
            aliases: new[] { "-v", "--verbose" },
            description: "Show verbose output");

        var noColorOption = new Option<bool>(
            aliases: new[] { "--no-color" },
            description: "Disable colored output");

        var command = new Command("init-schema", "Initialize business domain schema from BMMDL model");
        command.AddArgument(filesArg);
        command.AddOption(connectionOption);
        command.AddOption(resolveOption);
        command.AddOption(modulesDirOption);
        command.AddOption(forceOption);
        command.AddOption(dryRunOption);
        command.AddOption(verboseOption);
        command.AddOption(noColorOption);

        command.SetHandler(async (files, connection, resolveDeps, modulesDir, force, dryRun, verbose, noColor) =>
        {
            var output = new ConsoleCompilerOutput(!noColor);

            var filePaths = CommandHelper.ResolveFilePaths(files, resolveDeps, modulesDir, verbose, output);
            if (filePaths == null)
            {
                Environment.ExitCode = 1;
                return;
            }

            // Compile the model first
            var options = new CompilationOptions { Verbose = verbose, ShowProgress = true, UseColors = !noColor };
            var pipeline = new CompilerPipeline(options, output);
            var result = pipeline.Compile(filePaths);

            if (!result.Success || result.Context.Model == null)
            {
                output.WriteError("Compilation failed. Cannot initialize schema.");
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

            // Initialize schema
            var schemaService = new SchemaInitializationService(verbose, output);
            var success = await schemaService.InitializeSchemaAsync(
                result.Context.Model, connString, force, dryRun);

            Environment.ExitCode = success ? 0 : 1;
        }, filesArg, connectionOption, resolveOption, modulesDirOption, forceOption, dryRunOption, verboseOption, noColorOption);

        return command;
    }
}
