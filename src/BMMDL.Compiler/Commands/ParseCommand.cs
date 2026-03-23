using System.CommandLine;
using BMMDL.Compiler.Parsing;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Parses BMMDL files and displays the model structure.
/// Usage: bmmdlc parse &lt;files&gt;
/// </summary>
internal static class ParseCommand
{
    public static Command Create()
    {
        var filesArg = new Argument<FileInfo[]>("files", "BMMDL source files to parse")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var command = new Command("parse", "Parse BMMDL files and display the model structure");
        command.AddArgument(filesArg);

        command.SetHandler((files) =>
        {
            var validator = new BmmdlValidator(true);
            validator.ParseAndDisplay(files.Select(f => f.FullName));
        }, filesArg);

        return command;
    }
}
