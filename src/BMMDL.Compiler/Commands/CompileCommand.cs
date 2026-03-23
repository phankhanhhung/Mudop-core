using System.CommandLine;
using BMMDL.Compiler.Pipeline;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Compiles BMMDL source files to a target format (sql, json).
/// Usage: bmmdlc compile &lt;files&gt; [-o dir] [-t sql|json] [-v] [-r]
/// </summary>
internal static class CompileCommand
{
    public static Command Create()
    {
        var filesArg = new Argument<FileInfo[]>("files", "BMMDL source files to compile")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var outputOption = new Option<DirectoryInfo>(
            aliases: new[] { "-o", "--output" },
            description: "Output directory for generated files");

        var targetOption = new Option<string>(
            aliases: new[] { "-t", "--target" },
            description: "Target output format: csharp, sql, json",
            getDefaultValue: () => "csharp");

        var verboseOption = new Option<bool>(
            aliases: new[] { "-v", "--verbose" },
            description: "Show verbose output");

        var resolveOption = new Option<bool>(
            aliases: new[] { "-r", "--resolve-deps" },
            description: "Auto-resolve and include module dependencies");

        var command = new Command("compile", "Compile BMMDL source files to target format");
        command.AddArgument(filesArg);
        command.AddOption(outputOption);
        command.AddOption(targetOption);
        command.AddOption(verboseOption);
        command.AddOption(resolveOption);

        command.SetHandler((files, output, target, verbose, resolveDeps) =>
        {
            var consoleOutput = new ConsoleCompilerOutput(useColors: true);
            var filePaths = CommandHelper.ResolveFilePaths(files, resolveDeps, null, verbose, consoleOutput);
            if (filePaths == null)
            {
                Environment.ExitCode = 1;
                return;
            }

            // Compile
            var options = new CompilationOptions { Verbose = verbose, ShowProgress = true };
            var pipeline = new CompilerPipeline(options, consoleOutput);
            var result = pipeline.Compile(filePaths);

            if (!result.Success || result.Context.Model == null)
            {
                consoleOutput.WriteError($"Compilation failed with {result.ErrorCount} error(s)");
                Environment.ExitCode = 1;
                return;
            }

            var model = result.Context.Model;
            var outputDir = output?.FullName ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(outputDir);

            switch (target.ToLowerInvariant())
            {
                case "sql":
                {
                    var generator = new BMMDL.CodeGen.PostgresDdlGenerator(model);
                    var ddl = generator.GenerateFullSchema();
                    var outPath = Path.Combine(outputDir, "schema.sql");
                    File.WriteAllText(outPath, ddl);
                    consoleOutput.WriteSuccess($"DDL written to {outPath} ({ddl.Length:N0} chars)");
                    break;
                }
                case "json":
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(model,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    var outPath = Path.Combine(outputDir, "model.json");
                    File.WriteAllText(outPath, json);
                    consoleOutput.WriteSuccess($"Model JSON written to {outPath}");
                    break;
                }
                default:
                    consoleOutput.WriteError($"Unsupported target format: {target}. Use 'sql' or 'json'.");
                    Environment.ExitCode = 1;
                    break;
            }
        }, filesArg, outputOption, targetOption, verboseOption, resolveOption);

        return command;
    }
}
