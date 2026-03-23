using System.CommandLine;
using BMMDL.Compiler.Parsing;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Validates BMMDL source files for syntax and semantic errors.
/// Usage: bmmdlc validate &lt;files&gt; [-v]
/// </summary>
internal static class ValidateCommand
{
    public static Command Create()
    {
        var filesArg = new Argument<FileInfo[]>("files", "BMMDL source files to validate")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var verboseOption = new Option<bool>(
            aliases: new[] { "-v", "--verbose" },
            description: "Show verbose output");

        var command = new Command("validate", "Validate BMMDL source files for syntax and semantic errors");
        command.AddArgument(filesArg);
        command.AddOption(verboseOption);

        command.SetHandler((files, verbose) =>
        {
            var validator = new BmmdlValidator(verbose);
            var result = validator.ValidateFiles(files.Select(f => f.FullName));
            Environment.ExitCode = result ? 0 : 1;
        }, filesArg, verboseOption);

        return command;
    }
}
