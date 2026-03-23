using System.Text.RegularExpressions;

namespace BMMDL.Registry.Api.Services;

/// <summary>
/// Discovers BMMDL module files on disk.
/// Searches erp_modules/ directory structure for module.bmmdl files.
/// </summary>
public class ModuleDiscoveryService
{
    private readonly ILogger<ModuleDiscoveryService> _logger;

    public ModuleDiscoveryService(ILogger<ModuleDiscoveryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Find a module file by relative path, searching upward from known locations.
    /// </summary>
    public string? FindModulePath(string moduleRelativePath)
    {
        var relativePath = moduleRelativePath.Replace('/', Path.DirectorySeparatorChar);

        // Search upward from the app binary directory
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        // Search upward from current working directory
        dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        return null;
    }

    /// <summary>
    /// Discover all module.bmmdl files in erp_modules/ and map module names to file paths.
    /// </summary>
    public Dictionary<string, string> DiscoverModuleFiles(string erpModulesDir)
    {
        var moduleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subDir in Directory.GetDirectories(erpModulesDir))
        {
            var dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith("_")) continue; // Skip _archived etc.

            var moduleFile = Path.Combine(subDir, "module.bmmdl");
            if (!File.Exists(moduleFile)) continue;

            // Parse module name from "module ModuleName ..." declaration
            var firstLines = File.ReadLines(moduleFile).Take(20);
            foreach (var line in firstLines)
            {
                var match = Regex.Match(line, @"^\s*module\s+(\w+)");
                if (match.Success)
                {
                    moduleMap[match.Groups[1].Value] = moduleFile;
                    break;
                }
            }
        }

        _logger.LogInformation("Discovered {Count} modules in erp_modules/: {Names}",
            moduleMap.Count, string.Join(", ", moduleMap.Keys));

        return moduleMap;
    }

    /// <summary>
    /// Find the erp_modules/ directory by searching upward from known locations.
    /// </summary>
    public string? FindErpModulesDirectory()
    {
        // Search upward from app binary directory
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "erp_modules");
            if (Directory.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        // Search upward from current working directory
        dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "erp_modules");
            if (Directory.Exists(candidate))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        return null;
    }
}
