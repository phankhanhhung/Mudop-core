using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using BMMDL.Runtime.Plugins.Loading;
using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime.Plugins.Staging;

/// <summary>
/// Runs a series of validation checks against a staged plugin directory.
/// Checks are independent and all run even if earlier ones fail.
/// </summary>
public sealed class PluginValidationPipeline
{
    private readonly PlatformFeatureRegistry _registry;
    private readonly ILogger<PluginValidationPipeline> _logger;

    private static readonly Regex PluginNamePattern = new(@"^[a-zA-Z][a-zA-Z0-9._-]{0,98}[a-zA-Z0-9]$", RegexOptions.Compiled);
    private static readonly Regex SemVerPattern = new(@"^\d+\.\d+\.\d+(-[\w.]+)?(\+[\w.]+)?$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public PluginValidationPipeline(
        PlatformFeatureRegistry registry,
        ILogger<PluginValidationPipeline> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Run all validation checks against a staged plugin directory.
    /// Returns all check results (both passed and failed).
    /// </summary>
    public List<ValidationCheckResult> Validate(string pluginDirectory)
    {
        _logger.LogInformation("Starting validation for plugin directory: {PluginDirectory}", pluginDirectory);
        var results = new List<ValidationCheckResult>();

        // 1. Manifest check
        var (manifest, manifestResults) = ValidateManifest(pluginDirectory);
        results.AddRange(manifestResults);

        if (manifest == null)
        {
            _logger.LogWarning("Validation aborted: manifest not found or unparseable in {PluginDirectory}", pluginDirectory);
            return results;
        }

        // 2. Security check
        results.AddRange(ValidateSecurity(pluginDirectory, manifest));

        // 3. Assembly check
        results.AddRange(ValidateAssembly(pluginDirectory, manifest));

        // 4. Dependency check
        results.AddRange(ValidateDependencies(manifest));

        // 5. Compatibility check (no conflicting feature names)
        results.AddRange(ValidateCompatibility(manifest));

        // 6. BMMDL module check
        results.AddRange(ValidateBmmdlModules(pluginDirectory, manifest));

        var errorCount = results.Count(r => !r.Passed);
        var warnCount = results.Count(r => r.Severity == ValidationSeverity.Warning);
        _logger.LogInformation(
            "Validation complete for '{PluginName}': {TotalChecks} checks, {ErrorCount} errors, {WarnCount} warnings",
            manifest.Name, results.Count, errorCount, warnCount);

        return results;
    }

    /// <summary>
    /// Validate plugin.json manifest: presence, required fields, format.
    /// </summary>
    private (PluginManifestFile? Manifest, List<ValidationCheckResult> Results) ValidateManifest(string pluginDirectory)
    {
        const string check = "ManifestCheck";
        var results = new List<ValidationCheckResult>();

        var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            results.Add(ValidationCheckResult.Fail(check,
                "plugin.json not found in plugin root directory"));
            return (null, results);
        }

        PluginManifestFile manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<PluginManifestFile>(json, ManifestJsonOptions)!;
        }
        catch (Exception ex)
        {
            results.Add(ValidationCheckResult.Fail(check,
                $"Failed to parse plugin.json: {ex.Message}",
                ex.ToString()));
            return (null, results);
        }

        // Validate name
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            results.Add(ValidationCheckResult.Fail(check, "Plugin name is required in manifest"));
        }
        else if (!PluginNamePattern.IsMatch(manifest.Name))
        {
            results.Add(ValidationCheckResult.Fail(check,
                $"Plugin name '{manifest.Name}' is invalid. Must match pattern: letters, digits, dots, hyphens, underscores (2-100 chars, starts with letter)"));
        }
        else
        {
            results.Add(ValidationCheckResult.Pass(check, $"Plugin name '{manifest.Name}' is valid"));
        }

        // Validate version
        if (!SemVerPattern.IsMatch(manifest.Version))
        {
            results.Add(ValidationCheckResult.Warn(check,
                $"Plugin version '{manifest.Version}' does not follow semver (major.minor.patch). Consider using semver for consistency."));
        }
        else
        {
            results.Add(ValidationCheckResult.Pass(check, $"Version '{manifest.Version}' follows semver"));
        }

        // Validate description
        if (string.IsNullOrWhiteSpace(manifest.Description))
        {
            results.Add(ValidationCheckResult.Warn(check,
                "Plugin description is empty. Consider adding a description for discoverability."));
        }

        // Validate author
        if (string.IsNullOrWhiteSpace(manifest.Author))
        {
            results.Add(ValidationCheckResult.Warn(check,
                "Plugin author is not specified. Consider adding an author field."));
        }

        return (manifest, results);
    }

    /// <summary>
    /// Security checks: path traversal, suspicious files, directory structure.
    /// </summary>
    private List<ValidationCheckResult> ValidateSecurity(string pluginDirectory, PluginManifestFile manifest)
    {
        const string check = "SecurityCheck";
        var results = new List<ValidationCheckResult>();
        var pluginDirFull = Path.GetFullPath(pluginDirectory);

        // Check entry assembly path for traversal
        var entryAssembly = manifest.EntryAssembly ?? $"{manifest.Name}.dll";
        if (entryAssembly.Contains("..") || Path.IsPathRooted(entryAssembly))
        {
            results.Add(ValidationCheckResult.Fail(check,
                $"Entry assembly path '{entryAssembly}' contains path traversal. Must be relative to plugin directory."));
        }
        else
        {
            var resolvedPath = Path.GetFullPath(Path.Combine(pluginDirectory, entryAssembly));
            if (!resolvedPath.StartsWith(pluginDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(ValidationCheckResult.Fail(check,
                    $"Entry assembly path '{entryAssembly}' resolves outside plugin directory"));
            }
            else
            {
                results.Add(ValidationCheckResult.Pass(check, "Entry assembly path is safe"));
            }
        }

        // Check for symlinks in plugin directory
        try
        {
            var symlinks = Directory.GetFileSystemEntries(pluginDirectory, "*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var info = new FileInfo(path);
                    return info.LinkTarget != null;
                })
                .ToList();

            if (symlinks.Count > 0)
            {
                results.Add(ValidationCheckResult.Fail(check,
                    $"Plugin contains {symlinks.Count} symbolic link(s). Symlinks are not allowed for security.",
                    string.Join("\n", symlinks.Select(s => Path.GetRelativePath(pluginDirectory, s)))));
            }
            else
            {
                results.Add(ValidationCheckResult.Pass(check, "No symbolic links detected"));
            }
        }
        catch (Exception ex)
        {
            results.Add(ValidationCheckResult.Warn(check,
                $"Could not fully scan for symlinks: {ex.Message}"));
        }

        // Check for potentially dangerous file types
        var dangerousExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".ps1", ".sh", ".vbs", ".js", ".msi"
        };

        try
        {
            var dangerousFiles = Directory.GetFiles(pluginDirectory, "*", SearchOption.AllDirectories)
                .Where(f => dangerousExtensions.Contains(Path.GetExtension(f)))
                .Select(f => Path.GetRelativePath(pluginDirectory, f))
                .ToList();

            if (dangerousFiles.Count > 0)
            {
                results.Add(ValidationCheckResult.Warn(check,
                    $"Plugin contains {dangerousFiles.Count} potentially dangerous file(s): {string.Join(", ", dangerousFiles)}",
                    "Executable scripts and binaries should be reviewed before installation."));
            }
            else
            {
                results.Add(ValidationCheckResult.Pass(check, "No suspicious file types detected"));
            }
        }
        catch (Exception ex)
        {
            results.Add(ValidationCheckResult.Warn(check,
                $"Could not scan for suspicious files: {ex.Message}"));
        }

        return results;
    }

    /// <summary>
    /// Assembly check: entry DLL exists, is a valid .NET assembly, can be loaded.
    /// </summary>
    private List<ValidationCheckResult> ValidateAssembly(string pluginDirectory, PluginManifestFile manifest)
    {
        const string check = "AssemblyCheck";
        var results = new List<ValidationCheckResult>();

        var entryAssembly = manifest.EntryAssembly ?? $"{manifest.Name}.dll";
        var assemblyPath = Path.GetFullPath(Path.Combine(pluginDirectory, entryAssembly));

        if (!File.Exists(assemblyPath))
        {
            results.Add(ValidationCheckResult.Fail(check,
                $"Entry assembly '{entryAssembly}' not found in plugin directory"));
            return results;
        }

        results.Add(ValidationCheckResult.Pass(check, $"Entry assembly '{entryAssembly}' exists"));

        // Check file size
        var fileInfo = new FileInfo(assemblyPath);
        if (fileInfo.Length == 0)
        {
            results.Add(ValidationCheckResult.Fail(check, "Entry assembly is empty (0 bytes)"));
            return results;
        }

        // Validate assembly by loading in an isolated context for inspection
        try
        {
            var inspectContext = new AssemblyLoadContext($"validation-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                var assembly = inspectContext.LoadFromAssemblyPath(assemblyPath);

                // Check for IPlatformFeature implementations
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException rtle)
                {
                    // Some types couldn't load (missing deps), but we can still inspect loadable types
                    types = rtle.Types.Where(t => t != null).ToArray()!;
                    var failedCount = rtle.LoaderExceptions.Length;
                    results.Add(ValidationCheckResult.Warn(check,
                        $"{failedCount} type(s) could not be loaded (missing dependencies). Inspecting {types.Length} available types.",
                        string.Join("\n", rtle.LoaderExceptions.Select(e => e?.Message).Where(m => m != null).Distinct().Take(5))));
                }

                var featureTypes = types
                    .Where(t => !t.IsAbstract && !t.IsInterface &&
                           t.GetInterfaces().Any(i => i.Name == nameof(IPlatformFeature)))
                    .ToList();

                if (featureTypes.Count == 0)
                {
                    results.Add(ValidationCheckResult.Warn(check,
                        $"No {nameof(IPlatformFeature)} implementations found in entry assembly. " +
                        "Plugin may not contribute any features."));
                }
                else
                {
                    results.Add(ValidationCheckResult.Pass(check,
                        $"Found {featureTypes.Count} feature implementation(s): {string.Join(", ", featureTypes.Select(t => t.Name))}"));

                    // Check for parameterless constructors
                    foreach (var type in featureTypes)
                    {
                        var hasCtor = type.GetConstructor(Type.EmptyTypes) != null;
                        if (!hasCtor)
                        {
                            results.Add(ValidationCheckResult.Fail(check,
                                $"Feature type '{type.Name}' has no parameterless constructor. " +
                                "All IPlatformFeature implementations must have a parameterless constructor."));
                        }
                    }
                }
            }
            finally
            {
                inspectContext.Unload();
            }
        }
        catch (BadImageFormatException)
        {
            results.Add(ValidationCheckResult.Fail(check,
                $"'{entryAssembly}' is not a valid .NET assembly (BadImageFormatException)"));
        }
        catch (FileLoadException ex)
        {
            results.Add(ValidationCheckResult.Warn(check,
                $"Could not fully inspect assembly: {ex.Message}",
                "This may be due to missing dependencies. The plugin may still work when loaded at runtime."));
        }
        catch (Exception ex)
        {
            results.Add(ValidationCheckResult.Warn(check,
                $"Assembly inspection encountered an issue: {ex.Message}",
                ex.ToString()));
        }

        return results;
    }

    /// <summary>
    /// Dependency check: all declared dependencies exist in the registry.
    /// </summary>
    private List<ValidationCheckResult> ValidateDependencies(PluginManifestFile manifest)
    {
        const string check = "DependencyCheck";
        var results = new List<ValidationCheckResult>();

        if (manifest.Dependencies.Count == 0)
        {
            results.Add(ValidationCheckResult.Pass(check, "No dependencies declared"));
            return results;
        }

        var availableFeatures = _registry.AllFeatures.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var dep in manifest.Dependencies)
        {
            if (availableFeatures.Contains(dep))
            {
                results.Add(ValidationCheckResult.Pass(check, $"Dependency '{dep}' is available"));
            }
            else
            {
                missing.Add(dep);
            }
        }

        if (missing.Count > 0)
        {
            results.Add(ValidationCheckResult.Fail(check,
                $"Missing dependencies: {string.Join(", ", missing)}. " +
                "These must be installed before this plugin can be loaded."));
        }

        return results;
    }

    /// <summary>
    /// Compatibility check: no conflicting feature names with existing plugins.
    /// </summary>
    private List<ValidationCheckResult> ValidateCompatibility(PluginManifestFile manifest)
    {
        const string check = "CompatibilityCheck";
        var results = new List<ValidationCheckResult>();

        var existingFeatures = _registry.AllFeatures.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (existingFeatures.Contains(manifest.Name))
        {
            results.Add(ValidationCheckResult.Warn(check,
                $"A feature named '{manifest.Name}' already exists in the registry. " +
                "Installing this plugin will replace the existing feature."));
        }
        else
        {
            results.Add(ValidationCheckResult.Pass(check, "No feature name conflicts detected"));
        }

        return results;
    }

    /// <summary>
    /// Validate BMMDL modules: source files exist, paths are safe.
    /// </summary>
    private List<ValidationCheckResult> ValidateBmmdlModules(string pluginDirectory, PluginManifestFile manifest)
    {
        const string check = "BmmdlModuleCheck";
        var results = new List<ValidationCheckResult>();

        if (manifest.BmmdlModules.Count == 0)
        {
            results.Add(ValidationCheckResult.Pass(check, "No BMMDL modules declared"));
            return results;
        }

        var pluginDirFull = Path.GetFullPath(pluginDirectory);

        foreach (var module in manifest.BmmdlModules)
        {
            if (string.IsNullOrWhiteSpace(module.Name))
            {
                results.Add(ValidationCheckResult.Fail(check, "BMMDL module has empty name"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(module.SourceFile))
            {
                results.Add(ValidationCheckResult.Fail(check,
                    $"BMMDL module '{module.Name}' has no source file specified"));
                continue;
            }

            // Path traversal check
            if (module.SourceFile.Contains("..") || Path.IsPathRooted(module.SourceFile))
            {
                results.Add(ValidationCheckResult.Fail(check,
                    $"BMMDL module '{module.Name}' source path contains path traversal"));
                continue;
            }

            var sourcePath = Path.GetFullPath(Path.Combine(pluginDirectory, module.SourceFile));
            if (!sourcePath.StartsWith(pluginDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(ValidationCheckResult.Fail(check,
                    $"BMMDL module '{module.Name}' source path resolves outside plugin directory"));
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                results.Add(ValidationCheckResult.Fail(check,
                    $"BMMDL module '{module.Name}' source file not found: {module.SourceFile}"));
                continue;
            }

            // Try to read the file to ensure it's a valid text file
            try
            {
                var content = File.ReadAllText(sourcePath);
                if (string.IsNullOrWhiteSpace(content))
                {
                    results.Add(ValidationCheckResult.Warn(check,
                        $"BMMDL module '{module.Name}' source file is empty"));
                }
                else
                {
                    results.Add(ValidationCheckResult.Pass(check,
                        $"BMMDL module '{module.Name}' source file is valid ({content.Length} chars)"));
                }
            }
            catch (Exception ex)
            {
                results.Add(ValidationCheckResult.Fail(check,
                    $"Failed to read BMMDL module '{module.Name}' source: {ex.Message}"));
            }
        }

        return results;
    }
}
