using System.CommandLine;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler;

/// <summary>
/// BMMDL Compiler CLI - bmmdlc
/// Usage: bmmdlc validate <files>
///        bmmdlc compile <files> -o <output>
///        bmmdlc pipeline <files>  (multi-pass compilation)
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Initialize logging
        CompilerLoggerFactory.Initialize();

        var rootCommand = new RootCommand("BMMDL Compiler - Business Meta Model Definition Language");

        rootCommand.AddCommand(Commands.ValidateCommand.Create());
        rootCommand.AddCommand(Commands.CompileCommand.Create());
        rootCommand.AddCommand(Commands.ParseCommand.Create());
        rootCommand.AddCommand(Commands.PipelineCommand.Create());
        rootCommand.AddCommand(Commands.InitSchemaCommand.Create());
        rootCommand.AddCommand(Commands.InitSchemaFromRegistryCommand.Create());
        rootCommand.AddCommand(Commands.MigrateSchemaCommand.Create());
        rootCommand.AddCommand(Commands.RollbackSchemaCommand.Create());
        rootCommand.AddCommand(Commands.BootstrapCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }
}
