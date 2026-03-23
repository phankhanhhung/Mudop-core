using System.CommandLine;
using BMMDL.Compiler.Pipeline;
using BMMDL.Compiler.Services;
using BMMDL.Registry.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BMMDL.Compiler.Commands;

/// <summary>
/// Initializes the Mudop platform with Module 0 (System Tenant, Users, Roles).
/// Usage: bmmdlc bootstrap --init-platform [-c conn] [--platform-module path] [--seed-admin] [--admin-email] [--admin-password] [-v]
/// </summary>
internal static class BootstrapCommand
{
    public static Command Create()
    {
        var initPlatformOption = new Option<bool>(
            aliases: new[] { "--init-platform" },
            description: "Initialize platform with Module 0 (Tenant, User, Role, Permission)");

        var connectionOption = new Option<string?>(
            aliases: new[] { "-c", "--connection" },
            description: "Database connection string (uses POSTGRES_* env vars if not specified)");

        var platformModuleOption = new Option<string?>(
            aliases: new[] { "--platform-module" },
            () => "erp_modules/00_platform/module.bmmdl",
            description: "Path to Platform module file");

        var seedAdminOption = new Option<bool>(
            aliases: new[] { "--seed-admin" },
            description: "Create initial admin user after platform setup");

        var adminEmailOption = new Option<string?>(
            aliases: new[] { "--admin-email" },
            description: "Admin email (required with --seed-admin)");

        var adminPasswordOption = new Option<string?>(
            aliases: new[] { "--admin-password" },
            description: "Admin password (required with --seed-admin)");

        var verboseOption = new Option<bool>(
            aliases: new[] { "-v", "--verbose" },
            description: "Show verbose output");

        var command = new Command("bootstrap", "Initialize Mudop platform (Module 0 - System Tenant, Users, Roles)");
        command.AddOption(initPlatformOption);
        command.AddOption(connectionOption);
        command.AddOption(platformModuleOption);
        command.AddOption(seedAdminOption);
        command.AddOption(adminEmailOption);
        command.AddOption(adminPasswordOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (bool initPlatform, string? connection, string? platformModule,
                                   bool seedAdmin, string? adminEmail, string? adminPassword, bool verbose) =>
        {
            var logger = CompilerLoggerFactory.CreateLogger("Bootstrap");

            try
            {
                // Get connection string
                var connStr = connection ?? CommandHelper.GetConnectionStringFromEnv();
                if (string.IsNullOrEmpty(connStr))
                {
                    PrintBox("  \u2717 ERROR: No database connection string provided",
                             "  Use -c/--connection or set POSTGRES_* environment vars");
                    return;
                }

                if (!initPlatform)
                {
                    PrintUsage();
                    return;
                }

                Console.WriteLine("\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
                Console.WriteLine("\u2551  BMMDL Bootstrap - Platform Initialization                 \u2551");
                Console.WriteLine("\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
                Console.WriteLine();

                // System Tenant UUID (fixed)
                var systemTenantId = Guid.Parse("00000000-0000-0000-0000-000000000000");
                var systemTenantCode = "system";
                var systemTenantName = "System Tenant";

                // Step 1: Check if Platform module file exists
                var modulePath = platformModule ?? "erp_modules/00_platform/module.bmmdl";
                if (!File.Exists(modulePath))
                {
                    Console.WriteLine($"  \u2717 Platform module not found: {modulePath}");
                    return;
                }
                Console.WriteLine($"  \u2713 Found Platform module: {modulePath}");

                // Step 2: Compile Platform module
                Console.WriteLine("  \u25ba Compiling Platform module...");
                var pipeline = new CompilerPipeline();
                var compileResult = pipeline.Compile(new[] { modulePath });

                if (!compileResult.Success)
                {
                    Console.WriteLine($"  \u2717 Compilation failed with {compileResult.ErrorCount} error(s)");
                    return;
                }

                var model = compileResult.Context.Model;
                if (model == null)
                {
                    Console.WriteLine("  \u2717 Compilation produced no model");
                    return;
                }
                Console.WriteLine($"  \u2713 Compiled: {model.Entities.Count} entities, {model.Types.Count} types");

                // Step 3: Connect to Registry database and ensure schema exists
                Console.WriteLine("  \u25ba Connecting to Registry database...");
                var optionsBuilder = new DbContextOptionsBuilder<RegistryDbContext>();
                optionsBuilder.UseNpgsql(connStr);

                await using var dbContext = new RegistryDbContext(optionsBuilder.Options);

                // Ensure all registry tables exist (creates schema if missing)
                Console.WriteLine("  \u25ba Ensuring registry schema exists...");
                try
                {
                    // Try to check if tenants table exists by querying it
                    var anyTenant = await dbContext.Tenants.AnyAsync();
                    Console.WriteLine("  \u2713 Registry schema verified");
                }
                catch (Exception ex)
                {
                    // Table doesn't exist or other error, create all tables
                    Console.WriteLine($"  \u25ba Creating registry tables... ({ex.GetType().Name})");
                    var serviceProvider = ((Microsoft.EntityFrameworkCore.Infrastructure.IInfrastructure<IServiceProvider>)dbContext.Database).Instance;
                    var creator = (Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator)serviceProvider.GetService(typeof(Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator))!;
                    await creator.CreateTablesAsync();
                    Console.WriteLine("  \u2713 Registry tables created");
                }

                // Step 4: Create or verify System Tenant (search by ID, not name)
                Console.WriteLine("  \u25ba Checking System Tenant...");
                var existingTenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == systemTenantId);

                if (existingTenant == null)
                {
                    Console.WriteLine("  \u25ba Creating System Tenant...");
                    var tenant = new BMMDL.Registry.Entities.Tenant
                    {
                        Id = systemTenantId,
                        Name = systemTenantName,
                        Subdomain = systemTenantCode,
                        Settings = "{\"type\": \"system\", \"module0\": true}",
                        CreatedAt = DateTime.UtcNow
                    };
                    dbContext.Tenants.Add(tenant);

                    try
                    {
                        var rowsAffected = await dbContext.SaveChangesAsync();
                        if (verbose)
                            Console.WriteLine($"    (SaveChangesAsync returned {rowsAffected} rows affected)");
                    }
                    catch (Exception saveEx)
                    {
                        Console.WriteLine($"  \u2717 SaveChangesAsync failed: {saveEx.Message}");
                        if (verbose)
                            Console.WriteLine(saveEx.ToString());
                        return;
                    }

                    // Verify tenant was created (paranoia check for FK)
                    var verifyTenant = await dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == systemTenantId);
                    if (verifyTenant == null)
                    {
                        // Try to see what's actually in the table
                        var allTenants = await dbContext.Tenants.Take(5).ToListAsync();
                        Console.WriteLine($"  \u2717 Failed to verify tenant creation! (Found {allTenants.Count} other tenants)");
                        foreach (var t in allTenants)
                            Console.WriteLine($"    - {t.Id}: {t.Name}");
                        return;
                    }
                    Console.WriteLine($"  \u2713 Created System Tenant: {systemTenantName} ({systemTenantId})");
                }
                else
                {
                    Console.WriteLine($"  \u2713 System Tenant already exists: {existingTenant.Name} ({existingTenant.Id})");
                }

                // Step 5: Create Platform module record
                Console.WriteLine("  \u25ba Registering Platform module...");
                var existingModule = await dbContext.Modules.FirstOrDefaultAsync(
                    m => m.TenantId == systemTenantId && m.Name == "Platform");

                var moduleVersionStr = "1.0.0";
                if (existingModule == null)
                {
                    var newPlatformModule = new BMMDL.Registry.Entities.Module
                    {
                        Id = Guid.NewGuid(),
                        TenantId = systemTenantId,
                        Name = "Platform",
                        Version = moduleVersionStr,
                        Author = "Mudop Team",
                        Status = BMMDL.Registry.Entities.ModuleStatus.Published,
                        CreatedAt = DateTime.UtcNow,
                        PublishedAt = DateTime.UtcNow
                    };
                    dbContext.Modules.Add(newPlatformModule);
                    await dbContext.SaveChangesAsync();
                    Console.WriteLine($"  \u2713 Registered Platform module v{moduleVersionStr}");

                    // Step 5.5: Persist entity metadata to registry
                    Console.WriteLine("  \u25ba Publishing entity metadata to registry...");
                    var persistOutput = new ConsoleCompilerOutput(useColors: true);
                    var persistService = new DbPersistenceService(verbose, persistOutput);
                    var persistSuccess = await persistService.PublishAsync(model, systemTenantId, connStr);
                    if (persistSuccess)
                    {
                        Console.WriteLine($"  \u2713 Published {model.Entities.Count} entities to registry");
                    }
                    else
                    {
                        Console.WriteLine($"  \u26a0 Entity metadata publish failed (non-fatal, continuing...)");
                    }
                }
                else
                {
                    moduleVersionStr = existingModule.Version;
                    Console.WriteLine($"  \u2713 Platform module already registered: v{moduleVersionStr}");
                }

                // Step 6: Create Platform tables using SchemaInitializationService (same as other modules)
                Console.WriteLine($"  \u25ba Creating Platform tables ({model.Entities.Count} entities)...");
                var schemaOutput = new ConsoleCompilerOutput(useColors: true);
                var schemaService = new SchemaInitializationService(verbose, schemaOutput);
                var schemaSuccess = await schemaService.InitializeSchemaAsync(model, connStr, force: true);

                // Keep runtime for seeding
                var runtimeLogger = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<BMMDL.Runtime.PlatformRuntime>();
                var runtime = new BMMDL.Runtime.PlatformRuntime(connStr, connStr, runtimeLogger);

                // Step 7: Optionally seed admin user
                if (seedAdmin)
                {
                    if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
                    {
                        Console.WriteLine("  \u26a0 Skipping admin seed: --admin-email and --admin-password required");
                    }
                    else
                    {
                        Console.WriteLine("  \u25ba Seeding platform data...");
                        try
                        {
                            var seederLogger = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<BMMDL.Runtime.PlatformSeeder>();
                            var seeder = new BMMDL.Runtime.PlatformSeeder(runtime, seederLogger);
                            await seeder.SeedAllAsync(adminEmail, adminPassword);
                            Console.WriteLine($"  \u2713 Admin user seeded: {adminEmail}");
                            Console.WriteLine("  \u2713 Default roles created (SuperAdmin, TenantAdmin, User, Guest)");
                        }
                        catch (Exception seedEx)
                        {
                            Console.WriteLine($"  \u26a0 Seeding warning: {seedEx.Message}");
                        }
                    }
                }

                Console.WriteLine();
                Console.WriteLine("\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
                Console.WriteLine("\u2551  \u2713 Platform bootstrap completed successfully!              \u2551");
                Console.WriteLine("\u2560\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2563");
                Console.WriteLine($"\u2551  System Tenant: {systemTenantCode,-40} \u2551");
                Console.WriteLine($"\u2551  Module: Platform v{moduleVersionStr,-38} \u2551");
                Console.WriteLine($"\u2551  Entities: {model.Entities.Count,-46} \u2551");
                if (seedAdmin && !string.IsNullOrEmpty(adminEmail))
                    Console.WriteLine($"\u2551  Admin: {adminEmail,-49} \u2551");
                Console.WriteLine("\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n  \u2717 Bootstrap failed: {ex.Message}");
                if (verbose)
                {
                    Console.WriteLine($"\n{ex}");
                }
            }
        }, initPlatformOption, connectionOption, platformModuleOption,
           seedAdminOption, adminEmailOption, adminPasswordOption, verboseOption);

        return command;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
        Console.WriteLine("\u2551  BMMDL Bootstrap                                           \u2551");
        Console.WriteLine("\u2560\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2563");
        Console.WriteLine("\u2551  Usage:                                                    \u2551");
        Console.WriteLine("\u2551    bmmdlc bootstrap --init-platform                        \u2551");
        Console.WriteLine("\u2551                                                            \u2551");
        Console.WriteLine("\u2551  Options:                                                  \u2551");
        Console.WriteLine("\u2551    --init-platform    Initialize Module 0 (Platform)      \u2551");
        Console.WriteLine("\u2551    --seed-admin       Create initial admin user           \u2551");
        Console.WriteLine("\u2551    -c, --connection   Database connection string          \u2551");
        Console.WriteLine("\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
    }

    private static void PrintBox(params string[] lines)
    {
        Console.WriteLine("\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
        foreach (var line in lines)
            Console.WriteLine($"\u2551{line,-60}\u2551");
        Console.WriteLine("\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
    }
}
