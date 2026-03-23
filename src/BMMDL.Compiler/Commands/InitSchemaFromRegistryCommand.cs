using System.CommandLine;
using BMMDL.Compiler.Services;
using BMMDL.Registry.Data;
using BMMDL.Registry.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Initializes the business domain schema from a meta-model stored in the registry database.
/// Usage: bmmdlc init-schema-from-registry [-c conn] [--tenant id] [--force] [--dry-run] [-v] [--no-color]
/// </summary>
internal static class InitSchemaFromRegistryCommand
{
    public static Command Create()
    {
        var connectionOption = new Option<string?>(
            aliases: new[] { "-c", "--connection" },
            description: "Database connection string for BOTH registry AND business schema");

        var tenantOption = new Option<Guid?>(
            aliases: new[] { "--tenant" },
            description: "Tenant ID to load model from (default: system tenant)");

        var forceOption = new Option<bool>(
            aliases: new[] { "--force" },
            description: "Force recreation if schema exists");

        var dryRunOption = new Option<bool>(
            aliases: new[] { "--dry-run" },
            description: "Preview DDL without executing");

        var verboseOption = new Option<bool>(
            aliases: new[] { "-v", "--verbose" },
            description: "Show verbose output");

        var noColorOption = new Option<bool>(
            aliases: new[] { "--no-color" },
            description: "Disable colored output");

        var command = new Command("init-schema-from-registry",
            "Initialize business domain schema from meta-model stored in registry database");
        command.AddOption(connectionOption);
        command.AddOption(tenantOption);
        command.AddOption(forceOption);
        command.AddOption(dryRunOption);
        command.AddOption(verboseOption);
        command.AddOption(noColorOption);

        command.SetHandler(async (connection, tenant, force, dryRun, verbose, noColor) =>
        {
            var output = new ConsoleCompilerOutput(!noColor);
            var logger = CompilerLoggerFactory.CreateLogger("FromRegistry");

            // Get connection string
            var connString = CommandHelper.ResolveConnectionString(connection, output);
            if (string.IsNullOrEmpty(connString))
            {
                Environment.ExitCode = 1;
                return;
            }

            var tenantId = tenant ?? Guid.Parse("00000000-0000-0000-0000-000000000001");
            output.WriteLine($"\U0001f4e6 Loading meta-model from registry for tenant {tenantId}...");

            try
            {
                // Create DbContext and repository
                var optionsBuilder = new DbContextOptionsBuilder<RegistryDbContext>();
                optionsBuilder.UseNpgsql(connString);

                using var dbContext = new RegistryDbContext(optionsBuilder.Options);
                var repository = new EfCoreMetaModelRepository(dbContext, tenantId);

                // Load model from registry (tenantId passed to constructor)
                var model = await repository.LoadModelAsync();

                if (model == null || model.Entities.Count == 0)
                {
                    output.WriteWarning("No entities found in registry. Publish a module first.");
                    output.WriteWarning("Use: bmmdlc pipeline <module> -r -p -c <connection>");
                    Environment.ExitCode = 1;
                    return;
                }

                output.WriteSuccess($"Loaded {model.Entities.Count} entities, {model.AllModules.Count} modules");

                // Show loaded modules
                if (verbose)
                {
                    output.WriteLine("  Loaded modules:");
                    foreach (var mod in model.AllModules.OrderBy(m => m.Name))
                    {
                        output.WriteLine($"    \u2022 {mod.Name} v{mod.Version}");
                    }
                }

                // Initialize schema using existing service
                var schemaService = new SchemaInitializationService(verbose, output);
                var success = await schemaService.InitializeSchemaAsync(
                    model, connString, force, dryRun);

                Environment.ExitCode = success ? 0 : 1;
            }
            catch (Exception ex)
            {
                output.WriteError($"Failed to load model from registry: {ex.Message}");
                logger.LogError(ex, "Failed to load model from registry");
                if (verbose)
                    output.WriteLine(ex.ToString());
                Environment.ExitCode = 1;
            }
        }, connectionOption, tenantOption, forceOption, dryRunOption, verboseOption, noColorOption);

        return command;
    }
}
