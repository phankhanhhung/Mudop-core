using System.CommandLine;
using BMMDL.Compiler.Services;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Rolls back the last migration or a specific named migration.
/// Usage: bmmdlc rollback-schema [-c conn] [-n name] [-v] [--no-color]
/// </summary>
internal static class RollbackSchemaCommand
{
    public static Command Create()
    {
        var connectionOption = new Option<string?>(
            aliases: new[] { "-c", "--connection" },
            description: "Database connection string");

        var nameOption = new Option<string?>(
            aliases: new[] { "-n", "--name" },
            description: "Migration name to rollback (default: last migration)");

        var verboseOption = new Option<bool>(
            aliases: new[] { "-v", "--verbose" },
            description: "Show verbose output");

        var noColorOption = new Option<bool>(
            aliases: new[] { "--no-color" },
            description: "Disable colored output");

        var command = new Command("rollback-schema", "Rollback the last migration or a specific migration");
        command.AddOption(connectionOption);
        command.AddOption(nameOption);
        command.AddOption(verboseOption);
        command.AddOption(noColorOption);

        command.SetHandler(async (connection, name, verbose, noColor) =>
        {
            var output = new ConsoleCompilerOutput(!noColor);

            // Get connection string
            var connString = CommandHelper.ResolveConnectionString(connection, output);
            if (string.IsNullOrEmpty(connString))
            {
                Environment.ExitCode = 1;
                return;
            }

            // Rollback schema
            var schemaService = new SchemaInitializationService(verbose, output);
            var success = await schemaService.RollbackSchemaAsync(connString, name);

            Environment.ExitCode = success ? 0 : 1;
        }, connectionOption, nameOption, verboseOption, noColorOption);

        return command;
    }
}
