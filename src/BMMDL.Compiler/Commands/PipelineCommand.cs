using System.CommandLine;
using BMMDL.Compiler.Pipeline;
using BMMDL.Compiler.Services;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Runs the multi-pass compilation pipeline with optional dependency resolution and database publish.
/// Usage: bmmdlc pipeline &lt;files&gt; [-v] [--no-color] [-r] [-m dir] [-p] [-c conn] [--tenant id]
/// </summary>
internal static class PipelineCommand
{
    public static Command Create()
    {
        var filesArg = new Argument<FileInfo[]>("files", "BMMDL source files to compile")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var verboseOption = new Option<bool>(
            aliases: new[] { "-v", "--verbose" },
            description: "Show verbose output");

        var noColorOption = new Option<bool>(
            aliases: new[] { "--no-color" },
            description: "Disable colored output");

        var resolveOption = new Option<bool>(
            aliases: new[] { "-r", "--resolve-deps" },
            description: "Auto-resolve and include module dependencies");

        var modulesDirOption = new Option<string?>(
            aliases: new[] { "-m", "--modules-dir" },
            description: "Base directory for module discovery (auto-detected from file path if not specified)");

        var publishOption = new Option<bool>(
            aliases: new[] { "-p", "--publish" },
            description: "Publish compiled model to database");

        var connectionOption = new Option<string?>(
            aliases: new[] { "-c", "--connection" },
            description: "Database connection string (uses POSTGRES_* env vars if not specified)");

        var tenantOption = new Option<Guid?>(
            aliases: new[] { "--tenant" },
            description: "Tenant ID for multi-tenant deployment");

        var command = new Command("pipeline", "Run multi-pass compilation pipeline (recommended)");
        command.AddArgument(filesArg);
        command.AddOption(verboseOption);
        command.AddOption(noColorOption);
        command.AddOption(resolveOption);
        command.AddOption(modulesDirOption);
        command.AddOption(publishOption);
        command.AddOption(connectionOption);
        command.AddOption(tenantOption);

        command.SetHandler(async (files, verbose, noColor, resolveDeps, modulesDir, publish, connection, tenant) =>
        {
            var logger = CompilerLoggerFactory.CreateLogger("Pipeline");
            var output = new ConsoleCompilerOutput(!noColor);

            var filePaths = CommandHelper.ResolveFilePaths(
                files, resolveDeps, modulesDir, verbose, output, printTree: true);
            if (filePaths == null)
            {
                logger.LogError("Dependency resolution failed");
                Environment.ExitCode = 1;
                return;
            }

            var options = new CompilationOptions
            {
                Verbose = verbose,
                ShowProgress = true,
                UseColors = !noColor
            };

            var pipeline = new CompilerPipeline(options, output);
            var result = pipeline.Compile(filePaths);

            if (!result.Success)
            {
                Environment.ExitCode = 1;
                return;
            }

            // Publish to database if requested
            if (publish && result.Context.Model != null)
            {
                try
                {
                    var tenantId = tenant ?? Guid.Parse("00000000-0000-0000-0000-000000000001");
                    var dbService = new DbPersistenceService(verbose, output);
                    var success = await dbService.PublishAsync(result.Context.Model, tenantId, connection);
                    if (!success)
                    {
                        Environment.ExitCode = 1;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    output.WriteError($"Failed to publish to database: {ex.Message}");
                    logger.LogError(ex, "Database publish failed");
                    if (verbose)
                        output.WriteLine(ex.ToString());
                    Environment.ExitCode = 1;
                    return;
                }
            }

            Environment.ExitCode = 0;
        }, filesArg, verboseOption, noColorOption, resolveOption, modulesDirOption, publishOption, connectionOption, tenantOption);

        return command;
    }
}
