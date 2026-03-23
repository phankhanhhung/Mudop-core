using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace BMMDL.Runtime.Plugins.Loading;

/// <summary>
/// Scans a directory for plugin subdirectories, loads their assemblies via isolated
/// <see cref="PluginAssemblyLoadContext"/>s, discovers <see cref="IPlatformFeature"/>
/// implementations, and atomically rebuilds the <see cref="PlatformFeatureRegistry"/>.
///
/// Thread-safe: all mutations are serialized through <see cref="_lock"/>.
/// The registry rebuild is atomic (volatile snapshot swap inside PlatformFeatureRegistry).
///
/// <b>Dependency handling:</b>
/// <list type="number">
///   <item>Pre-flight: <c>manifest.Dependencies</c> are topologically sorted before any DLL is loaded.</item>
///   <item>Plugins with unresolvable manifest dependencies are skipped (no assembly loaded).</item>
///   <item>Post-load: discovered <c>IPlatformFeature.DependsOn</c> is cross-validated against manifest; mismatches are logged as warnings.</item>
///   <item>Registry rebuild: <c>TopologicalSort</c> uses <c>IPlatformFeature.DependsOn</c> (code) as the authoritative source.</item>
///   <item>Unload: blocked if another loaded plugin depends on the target.</item>
/// </list>
///
/// Supports both directory-based and zip-based plugin packaging:
/// <code>
/// {PluginsDirectory}/
/// ├── MyPlugin/                     ← directory-based
/// │   ├── plugin.json               ← manifest (required)
/// │   ├── MyPlugin.dll              ← entry assembly
/// │   └── ...                       ← other dependencies
/// ├── AnotherPlugin.zip             ← zip-based (auto-extracted)
/// │   └── (contains plugin.json + DLLs at root or in single subdirectory)
/// └── AnotherPlugin/                ← extracted zip
///     ├── plugin.json
///     └── AnotherPlugin.dll
/// </code>
///
/// Zip archives are extracted to subdirectories during scan. The zip must contain
/// <c>plugin.json</c> either at the archive root or inside a single top-level directory.
/// </summary>
public sealed class PluginDirectoryLoader : IDisposable
{
    private readonly string _pluginsDirectory;
    private readonly PlatformFeatureRegistry _registry;
    private readonly IReadOnlyList<IPlatformFeature> _builtInFeatures;
    private readonly ILogger<PluginDirectoryLoader> _logger;
    private readonly HashSet<string> _preLoadedPlugins;
    private readonly object _lock = new();
    private readonly Dictionary<string, PluginDescriptor> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public PluginDirectoryLoader(
        string pluginsDirectory,
        PlatformFeatureRegistry registry,
        IReadOnlyList<IPlatformFeature> builtInFeatures,
        ILogger<PluginDirectoryLoader> logger,
        HashSet<string>? preLoadedPluginNames = null)
    {
        _pluginsDirectory = Path.GetFullPath(pluginsDirectory);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _builtInFeatures = builtInFeatures ?? throw new ArgumentNullException(nameof(builtInFeatures));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preLoadedPlugins = preLoadedPluginNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// All currently loaded external plugins, keyed by plugin name.
    /// </summary>
    public IReadOnlyDictionary<string, PluginDescriptor> LoadedPlugins
    {
        get
        {
            lock (_lock)
                return new Dictionary<string, PluginDescriptor>(_loadedPlugins, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Scans the plugins directory for all plugin subdirectories containing a <c>plugin.json</c>,
    /// loads assemblies for those with <c>autoLoad: true</c>, discovers features, and rebuilds
    /// the registry.
    ///
    /// Plugins are loaded in dependency order (topological sort of manifest dependencies).
    /// Plugins with unresolvable dependencies are skipped entirely — no DLL is loaded.
    ///
    /// Safe to call multiple times — already-loaded plugins are skipped.
    /// </summary>
    /// <returns>List of plugin names that were newly loaded.</returns>
    public IReadOnlyList<string> ScanAndLoadAll()
    {
        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogInformation("Plugins directory does not exist, creating: {Path}", _pluginsDirectory);
            Directory.CreateDirectory(_pluginsDirectory);
            return [];
        }

        // ── Phase 0: Extract any .zip archives ──────────────────────
        ExtractZipPlugins();

        // ── Phase 1: Read all manifests ─────────────────────────────
        var candidates = new List<(string Directory, PluginManifestFile Manifest)>();
        var subdirectories = Directory.GetDirectories(_pluginsDirectory);

        _logger.LogInformation("Scanning plugins directory: {Path} ({Count} subdirectories)",
            _pluginsDirectory, subdirectories.Length);

        foreach (var dir in subdirectories)
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                _logger.LogDebug("Skipping {Dir} — no plugin.json found", Path.GetFileName(dir));
                continue;
            }

            try
            {
                var manifest = LoadManifestFile(manifestPath);
                if (!manifest.AutoLoad)
                {
                    _logger.LogDebug("Skipping plugin '{Name}' — autoLoad is false", manifest.Name);
                    continue;
                }

                lock (_lock)
                {
                    if (_loadedPlugins.ContainsKey(manifest.Name))
                    {
                        _logger.LogDebug("Plugin '{Name}' already loaded — skipping", manifest.Name);
                        continue;
                    }
                }

                // Skip plugins whose services were pre-registered during DI configuration.
                // Their assemblies were already loaded via Assembly.LoadFrom in the pre-scan phase;
                // full PluginDirectoryLoader loading (isolated AssemblyLoadContext) is not needed.
                if (_preLoadedPlugins.Contains(manifest.Name))
                {
                    _logger.LogDebug(
                        "Plugin '{Name}' was pre-loaded during service registration — skipping full load",
                        manifest.Name);
                    continue;
                }

                candidates.Add((dir, manifest));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read manifest from {Dir}", dir);
            }
        }

        if (candidates.Count == 0)
            return [];

        // ── Phase 2: Sort by manifest dependencies ──────────────────
        var sorted = SortManifestsByDependency(candidates);

        // ── Phase 3: Load assemblies in dependency order ────────────
        var loaded = new List<string>();

        foreach (var (dir, manifest) in sorted)
        {
            try
            {
                var descriptor = LoadPlugin(dir, manifest);
                if (descriptor is not null)
                {
                    loaded.Add(descriptor.Manifest.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from {Dir}", dir);
            }
        }

        if (loaded.Count > 0)
        {
            // Rebuild inside lock to prevent TOCTOU gap where another thread
            // modifies _loadedPlugins between the last LoadPlugin and rebuild.
            lock (_lock)
            {
                RebuildRegistryLocked();
            }
            _logger.LogInformation("Loaded {Count} external plugin(s): {Names}",
                loaded.Count, string.Join(", ", loaded));
        }

        return loaded;
    }

    /// <summary>
    /// Loads a single plugin from the specified directory.
    /// If the directory contains a valid <c>plugin.json</c> and entry assembly,
    /// the plugin's features are discovered and the registry is rebuilt.
    ///
    /// All manifest dependencies must be satisfied (available as built-in or already loaded)
    /// before loading. Throws <see cref="InvalidOperationException"/> if not.
    /// </summary>
    /// <returns>The plugin descriptor, or null if loading failed.</returns>
    public PluginDescriptor? LoadPluginFromDirectory(string pluginDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);

        var dir = Path.GetFullPath(pluginDirectory);

        // V2: Prevent path traversal — plugin directory must be within the configured plugins root.
        // Append DirectorySeparatorChar to prevent "/pluginsevil/" matching "/plugins".
        if (!dir.StartsWith(_pluginsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dir, _pluginsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Plugin path must be within the plugins directory '{_pluginsDirectory}'. Got: '{dir}'");
        }

        var manifestPath = Path.Combine(dir, "plugin.json");

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"plugin.json not found in {dir}");

        var manifest = LoadManifestFile(manifestPath);

        lock (_lock)
        {
            if (_loadedPlugins.ContainsKey(manifest.Name))
                throw new InvalidOperationException($"Plugin '{manifest.Name}' is already loaded");

            // Pre-flight: verify all manifest dependencies are satisfied (inside lock
            // to prevent TOCTOU race where dependencies change between check and load)
            ValidateManifestDependencies(manifest);
        }

        var descriptor = LoadPlugin(dir, manifest);
        if (descriptor is not null)
        {
            // Rebuild inside lock to prevent TOCTOU gap where another thread
            // modifies _loadedPlugins between LoadPlugin and rebuild.
            lock (_lock)
            {
                RebuildRegistryLocked();
            }
        }

        return descriptor;
    }

    /// <summary>
    /// Loads a single plugin from a <c>.zip</c> archive.
    /// The zip is extracted to the plugins directory, then loaded normally.
    ///
    /// The zip must contain <c>plugin.json</c> either at the archive root or inside
    /// a single top-level directory. If a directory with the same plugin name already
    /// exists, it is overwritten (upgrade scenario).
    /// </summary>
    /// <returns>The plugin descriptor, or null if loading failed.</returns>
    public PluginDescriptor? LoadPluginFromZip(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);

        var fullZipPath = Path.GetFullPath(zipPath);

        // V2: Prevent path traversal — zip must be within or targeting the plugins directory.
        // Append DirectorySeparatorChar to prevent "/pluginsevil/" matching "/plugins".
        if (!fullZipPath.StartsWith(_pluginsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullZipPath, _pluginsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Plugin zip path must be within the plugins directory '{_pluginsDirectory}'. Got: '{fullZipPath}'");
        }

        if (!File.Exists(fullZipPath))
            throw new FileNotFoundException($"Plugin zip file not found: {fullZipPath}");

        if (!fullZipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"File is not a .zip archive: {fullZipPath}");

        var extractedDir = ExtractZipToDirectory(fullZipPath);

        try
        {
            return LoadPluginFromDirectory(extractedDir);
        }
        catch
        {
            // Clean up extracted directory if plugin loading fails after extraction
            if (Directory.Exists(extractedDir))
            {
                try { Directory.Delete(extractedDir, recursive: true); }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx,
                        "Failed to clean up extracted plugin directory after load failure: {Dir}",
                        extractedDir);
                }
            }
            throw;
        }
    }

    /// <summary>
    /// Loads a single plugin from a zip archive provided as a <see cref="Stream"/>.
    /// Useful for HTTP file uploads where the zip content is streamed directly.
    ///
    /// The zip must contain <c>plugin.json</c> either at the archive root or inside
    /// a single top-level directory.
    /// </summary>
    /// <param name="zipStream">Stream containing the zip archive data.</param>
    /// <param name="originalFileName">Original file name (used for logging). Optional.</param>
    /// <returns>The plugin descriptor, or null if loading failed.</returns>
    public PluginDescriptor? LoadPluginFromZipStream(Stream zipStream, string? originalFileName = null)
    {
        ArgumentNullException.ThrowIfNull(zipStream);

        // Save stream to a temp .zip file, then extract
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"bmmdl_plugin_{Guid.NewGuid():N}.zip");
        string? extractedDir = null;

        try
        {
            using (var fileStream = File.Create(tempZipPath))
            {
                zipStream.CopyTo(fileStream);
            }

            _logger.LogInformation("Saved uploaded plugin zip to {Path} (original: {OriginalName})",
                tempZipPath, originalFileName ?? "unknown");

            extractedDir = ExtractZipToDirectory(tempZipPath);

            try
            {
                return LoadPluginFromDirectory(extractedDir);
            }
            catch
            {
                // Clean up extracted directory if plugin loading fails after extraction
                if (Directory.Exists(extractedDir))
                {
                    try { Directory.Delete(extractedDir, recursive: true); }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx,
                            "Failed to clean up extracted plugin directory after load failure: {Dir}",
                            extractedDir);
                    }
                }
                throw;
            }
        }
        finally
        {
            // Clean up temp zip file (extracted directory stays in plugins dir)
            try { File.Delete(tempZipPath); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Unloads a previously loaded external plugin by name.
    /// Removes its features from the registry and disposes the assembly load context.
    ///
    /// Blocked if any other loaded plugin depends on this one (via manifest or code dependencies).
    /// </summary>
    public void UnloadPlugin(string pluginName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        PluginDescriptor descriptor;

        lock (_lock)
        {
            if (!_loadedPlugins.TryGetValue(pluginName, out descriptor!))
                throw new InvalidOperationException($"Plugin '{pluginName}' is not loaded");

            // Check both manifest dependencies AND code DependsOn
            foreach (var (name, other) in _loadedPlugins)
            {
                if (name == pluginName) continue;

                // Check manifest-level
                if (other.Manifest.Dependencies.Contains(pluginName, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Cannot unload plugin '{pluginName}': plugin '{name}' depends on it (manifest)");

                // Check code-level
                foreach (var feature in other.Features)
                {
                    if (feature.DependsOn.Contains(pluginName, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Cannot unload plugin '{pluginName}': feature '{feature.Name}' in plugin '{name}' depends on it (code)");
                }
            }

            _loadedPlugins.Remove(pluginName);

            // Rebuild inside lock so registry is consistent with _loadedPlugins state.
            // No TOCTOU gap between Remove and Rebuild.
            RebuildRegistryLocked();
        }

        _logger.LogInformation(
            "Unloading plugin '{Name}' assembly context (GC will collect on next cycle)",
            pluginName);
        descriptor.Dispose();
    }

    /// <summary>
    /// Starts watching the plugins directory for new plugin subdirectories.
    /// When a new directory with a <c>plugin.json</c> is created, the plugin is
    /// automatically loaded (if autoLoad is true).
    /// </summary>
    public void EnableDirectoryWatching()
    {
        if (_watcher is not null) return;

        if (!Directory.Exists(_pluginsDirectory))
            Directory.CreateDirectory(_pluginsDirectory);

        _watcher = new FileSystemWatcher(_pluginsDirectory)
        {
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnPluginDirectoryCreated;
        _logger.LogInformation("Watching plugins directory for new plugins: {Path}", _pluginsDirectory);
    }

    /// <summary>
    /// Stops watching the plugins directory.
    /// </summary>
    public void DisableDirectoryWatching()
    {
        if (_watcher is null) return;

        _watcher.Created -= OnPluginDirectoryCreated;
        _watcher.Dispose();
        _watcher = null;
        _logger.LogInformation("Stopped watching plugins directory");
    }

    public void Dispose()
    {
        DisableDirectoryWatching();

        lock (_lock)
        {
            foreach (var descriptor in _loadedPlugins.Values)
            {
                try { descriptor.Dispose(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing plugin '{Name}'", descriptor.Manifest.Name);
                }
            }
            _loadedPlugins.Clear();
        }
    }

    // ── Private helpers ──────────────────────────────────────────

    /// <summary>
    /// Topological sort of manifest candidates by their <c>Dependencies</c>.
    /// Built-in features and already-loaded plugins count as "available".
    /// Plugins whose dependencies cannot be resolved are dropped with an error log.
    /// </summary>
    private List<(string Directory, PluginManifestFile Manifest)> SortManifestsByDependency(
        List<(string Directory, PluginManifestFile Manifest)> candidates)
    {
        if (candidates.Count <= 1)
            return candidates;

        var available = GetAvailableFeatureNames();
        var byName = new Dictionary<string, (string Dir, PluginManifestFile Manifest)>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in candidates)
        {
            if (!byName.TryAdd(item.Manifest.Name, item))
            {
                _logger.LogError("Duplicate plugin name '{Name}' — skipping second occurrence", item.Manifest.Name);
                continue;
            }
            inDegree[item.Manifest.Name] = 0;
            dependents[item.Manifest.Name] = [];
        }

        // Track plugins that must be skipped due to unresolvable deps
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in candidates)
        {
            if (!byName.ContainsKey(item.Manifest.Name))
                continue; // duplicate, already skipped

            foreach (var dep in item.Manifest.Dependencies)
            {
                if (available.Contains(dep))
                    continue; // satisfied by built-in or already-loaded

                if (!byName.ContainsKey(dep))
                {
                    _logger.LogError(
                        "Plugin '{Name}' depends on '{Dep}' which is not available (not built-in, not loaded, not in scan batch) — skipping",
                        item.Manifest.Name, dep);
                    skip.Add(item.Manifest.Name);
                    break;
                }

                // Intra-batch dependency — add graph edge
                dependents[dep].Add(item.Manifest.Name);
                inDegree[item.Manifest.Name]++;
            }
        }

        // Remove skipped plugins and cascade (anything depending on a skipped plugin is also skipped)
        if (skip.Count > 0)
            CascadeSkip(skip, byName, dependents, inDegree);

        // Kahn's algorithm
        var queue = new Queue<string>();
        foreach (var (name, deg) in inDegree)
        {
            if (deg == 0 && !skip.Contains(name))
                queue.Enqueue(name);
        }

        var sorted = new List<(string Directory, PluginManifestFile Manifest)>();
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            sorted.Add(byName[name]);

            foreach (var dependent in dependents[name])
            {
                if (skip.Contains(dependent)) continue;
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        // Detect cycles: any non-skipped nodes with inDegree > 0
        var cyclic = inDegree
            .Where(kv => kv.Value > 0 && !skip.Contains(kv.Key))
            .Select(kv => kv.Key)
            .ToList();

        if (cyclic.Count > 0)
        {
            _logger.LogError(
                "Circular dependency detected among plugins: {Plugins} — these plugins will not be loaded",
                string.Join(", ", cyclic));
        }

        if (sorted.Count < candidates.Count)
        {
            var skippedNames = candidates
                .Select(c => c.Manifest.Name)
                .Where(n => !sorted.Any(s => s.Manifest.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _logger.LogWarning(
                "Skipped {Count} plugin(s) due to dependency issues: {Names}",
                skippedNames.Count, string.Join(", ", skippedNames));
        }

        return sorted;
    }

    /// <summary>
    /// Cascades skip set: if A is skipped and B depends on A, B is also skipped, etc.
    /// </summary>
    private void CascadeSkip(
        HashSet<string> skip,
        Dictionary<string, (string Dir, PluginManifestFile Manifest)> byName,
        Dictionary<string, List<string>> dependents,
        Dictionary<string, int> inDegree)
    {
        var queue = new Queue<string>(skip);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!dependents.TryGetValue(name, out var deps)) continue;

            foreach (var dependent in deps)
            {
                if (skip.Add(dependent))
                {
                    _logger.LogError(
                        "Plugin '{Name}' also skipped because its dependency '{Dep}' was skipped",
                        dependent, name);
                    queue.Enqueue(dependent);
                }
            }
        }
    }

    /// <summary>
    /// Returns the set of feature names currently available (built-in + loaded plugins).
    /// Acquires <see cref="_lock"/> internally — do NOT call from within a lock.
    /// </summary>
    private HashSet<string> GetAvailableFeatureNames()
    {
        lock (_lock)
        {
            return GetAvailableFeatureNamesLocked();
        }
    }

    /// <summary>
    /// Returns the set of feature names currently available (built-in + loaded plugins).
    /// Caller MUST already hold <see cref="_lock"/>. This avoids deadlock when called
    /// from methods that already acquired the lock (e.g., <see cref="ValidateManifestDependencies"/>).
    /// </summary>
    private HashSet<string> GetAvailableFeatureNamesLocked()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in _builtInFeatures)
            names.Add(f.Name);

        foreach (var name in _loadedPlugins.Keys)
            names.Add(name);

        // Also add individual feature names from loaded plugins
        // (a plugin may expose features with names different from the plugin name)
        foreach (var descriptor in _loadedPlugins.Values)
        {
            foreach (var feature in descriptor.Features)
                names.Add(feature.Name);
        }

        return names;
    }

    /// <summary>
    /// Validates that all manifest dependencies of a single plugin are currently satisfied.
    /// Throws <see cref="InvalidOperationException"/> with a clear message if not.
    /// Caller MUST already hold <see cref="_lock"/> (uses <see cref="GetAvailableFeatureNamesLocked"/>).
    /// </summary>
    private void ValidateManifestDependencies(PluginManifestFile manifest)
    {
        if (manifest.Dependencies.Count == 0)
            return;

        var available = GetAvailableFeatureNamesLocked();
        var missing = manifest.Dependencies
            .Where(dep => !available.Contains(dep))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Plugin '{manifest.Name}' cannot be loaded: missing dependencies: {string.Join(", ", missing)}. " +
                $"Available features: {string.Join(", ", available.OrderBy(n => n))}");
        }
    }

    private PluginDescriptor? LoadPlugin(string directory, PluginManifestFile manifest)
    {
        var entryDll = manifest.EntryAssembly ?? $"{manifest.Name}.dll";

        // Validate entryDll doesn't contain path traversal
        if (entryDll.Contains("..") || entryDll.Contains('/') || entryDll.Contains('\\') ||
            Path.IsPathRooted(entryDll))
        {
            _logger.LogError("Plugin '{Name}' has invalid entry assembly path: {Path}", manifest.Name, entryDll);
            return null;
        }

        var assemblyPath = Path.Combine(directory, entryDll);

        // Defense-in-depth: verify resolved path is within the plugin directory
        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        var fullDirectory = Path.GetFullPath(directory);
        if (!fullAssemblyPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !fullAssemblyPath.Equals(fullDirectory, StringComparison.Ordinal))
        {
            _logger.LogError("Plugin '{Name}' entry assembly escapes plugin directory: {Path}", manifest.Name, assemblyPath);
            return null;
        }

        if (!File.Exists(assemblyPath))
        {
            _logger.LogError("Entry assembly not found for plugin '{Name}': {Path}",
                manifest.Name, assemblyPath);
            return null;
        }

        lock (_lock)
        {
            // Double-check inside lock to prevent race: two threads loading same plugin
            // would both create AssemblyLoadContexts, first adds to dict, second overwrites
            // and leaks the first ALC.
            if (_loadedPlugins.ContainsKey(manifest.Name))
                throw new InvalidOperationException($"Plugin '{manifest.Name}' is already loaded");

            _logger.LogDebug("Loading plugin '{Name}' from {Path}", manifest.Name, assemblyPath);

            // Create isolated load context and load the assembly INSIDE lock.
            // If anything fails after ALC creation, we must unload it to prevent leaks.
            var loadContext = new PluginAssemblyLoadContext(assemblyPath);
            var keepContext = false;

            try
            {
                Assembly assembly;

                try
                {
                    assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load assembly for plugin '{Name}'", manifest.Name);
                    return null; // finally will unload
                }

                // Discover IPlatformFeature implementations
                List<IPlatformFeature> features;
                try
                {
                    features = DiscoverFeatures(assembly, manifest.Name);
                }
                catch (TypeLoadException ex)
                {
                    _logger.LogError(ex,
                        "Type loading failed during feature discovery for plugin '{Name}'",
                        manifest.Name);
                    return null; // finally will unload
                }
                catch (BadImageFormatException ex)
                {
                    _logger.LogError(ex,
                        "Bad image format during feature discovery for plugin '{Name}'",
                        manifest.Name);
                    return null; // finally will unload
                }
                catch (ReflectionTypeLoadException ex)
                {
                    _logger.LogError(ex,
                        "Reflection type loading failed during feature discovery for plugin '{Name}'",
                        manifest.Name);
                    return null; // finally will unload
                }

                if (features.Count == 0)
                {
                    _logger.LogWarning(
                        "Plugin '{Name}' has no IPlatformFeature implementations — loaded but inactive",
                        manifest.Name);
                }

                // Cross-validate manifest deps vs code deps
                CrossValidateDependencies(manifest, features);

                var descriptor = new PluginDescriptor
                {
                    Manifest = manifest,
                    DirectoryPath = directory,
                    AssemblyPath = assemblyPath,
                    LoadContext = loadContext,
                    Assembly = assembly,
                    Features = features
                };

                _loadedPlugins[manifest.Name] = descriptor;

                _logger.LogInformation(
                    "Loaded plugin '{Name}' v{Version} with {Count} feature(s): {Features}",
                    manifest.Name, manifest.Version, features.Count,
                    string.Join(", ", features.Select(f => f.Name)));

                // Success — prevent finally from unloading the context
                keepContext = true;
                return descriptor;
            }
            finally
            {
                // Unload the ALC on any error path to prevent AssemblyLoadContext leak.
                // Only skip unload when keepContext is explicitly set to true (success path).
                if (!keepContext)
                {
                    loadContext.Unload();
                }
            }
        }
    }

    /// <summary>
    /// Cross-validates manifest <c>dependencies</c> against the actual <c>IPlatformFeature.DependsOn</c>
    /// declared in code. Logs warnings for mismatches so developers can keep manifests in sync.
    /// </summary>
    private void CrossValidateDependencies(PluginManifestFile manifest, List<IPlatformFeature> features)
    {
        // Collect all code-level DependsOn from discovered features
        var codeDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in features)
        {
            foreach (var dep in feature.DependsOn)
                codeDeps.Add(dep);
        }

        var manifestDeps = new HashSet<string>(manifest.Dependencies, StringComparer.OrdinalIgnoreCase);

        // Deps declared in code but missing from manifest
        var missingInManifest = codeDeps.Except(manifestDeps, StringComparer.OrdinalIgnoreCase).ToList();
        if (missingInManifest.Count > 0)
        {
            _logger.LogWarning(
                "Plugin '{Name}': code declares DependsOn [{CodeDeps}] but manifest is missing [{Missing}]. " +
                "Update plugin.json to match.",
                manifest.Name,
                string.Join(", ", codeDeps),
                string.Join(", ", missingInManifest));
        }

        // Deps declared in manifest but not in any feature's code
        var extraInManifest = manifestDeps.Except(codeDeps, StringComparer.OrdinalIgnoreCase).ToList();
        if (extraInManifest.Count > 0)
        {
            _logger.LogWarning(
                "Plugin '{Name}': manifest declares dependencies [{ManifestDeps}] but code does not depend on [{Extra}]. " +
                "Consider removing from plugin.json or adding DependsOn in code.",
                manifest.Name,
                string.Join(", ", manifestDeps),
                string.Join(", ", extraInManifest));
        }
    }

    /// <summary>
    /// Discovers all concrete types implementing <see cref="IPlatformFeature"/> in the assembly,
    /// creates instances via parameterless constructors.
    /// </summary>
    private List<IPlatformFeature> DiscoverFeatures(Assembly assembly, string pluginName)
    {
        var features = new List<IPlatformFeature>();
        var featureType = typeof(IPlatformFeature);

        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogWarning(ex,
                "Some types in plugin '{Name}' could not be loaded. Proceeding with available types.",
                pluginName);
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            try
            {
                if (!featureType.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                    continue;

                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor is null)
                {
                    _logger.LogWarning(
                        "Plugin '{Name}': type {Type} implements IPlatformFeature but has no parameterless constructor — skipped",
                        pluginName, type.FullName);
                    continue;
                }

                var instance = (IPlatformFeature)Activator.CreateInstance(type)!;
                features.Add(instance);
                _logger.LogDebug("Discovered feature '{Feature}' in plugin '{Plugin}'",
                    instance.Name, pluginName);
            }
            catch (Exception ex)
            {
                // Catch all reflection/instantiation errors per type so that one bad type
                // does not abort discovery of remaining features (and does not leak the ALC
                // by letting an unhandled exception propagate out of DiscoverFeatures).
                _logger.LogError(ex,
                    "Plugin '{Name}': failed to inspect or instantiate {Type}",
                    pluginName, type.FullName);
            }
        }

        return features;
    }

    /// <summary>
    /// Atomically rebuilds the <see cref="PlatformFeatureRegistry"/> with all built-in features
    /// plus all currently loaded external plugin features.
    ///
    /// Acquires <see cref="_lock"/> internally. Use <see cref="RebuildRegistryLocked"/> when
    /// the caller already holds the lock.
    /// </summary>
    private void RebuildRegistry()
    {
        lock (_lock)
        {
            RebuildRegistryLocked();
        }
    }

    /// <summary>
    /// Rebuilds the registry. Caller MUST already hold <see cref="_lock"/>.
    /// </summary>
    private void RebuildRegistryLocked()
    {
        var allFeatures = new List<IPlatformFeature>(_builtInFeatures);
        foreach (var descriptor in _loadedPlugins.Values)
        {
            allFeatures.AddRange(descriptor.Features);
        }

        _registry.Rebuild(allFeatures);

        _logger.LogDebug("Registry rebuilt with {Count} total features ({BuiltIn} built-in + {External} external)",
            allFeatures.Count, _builtInFeatures.Count, allFeatures.Count - _builtInFeatures.Count);
    }

    private static PluginManifestFile LoadManifestFile(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PluginManifestFile>(json, ManifestJsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize plugin.json at {path}");
    }

    /// <summary>
    /// Scans the plugins directory for <c>.zip</c> files and extracts each one.
    /// Already-extracted plugins (matching subdirectory exists) are skipped.
    /// Successfully extracted zips are deleted to prevent re-extraction on next scan.
    /// </summary>
    private void ExtractZipPlugins()
    {
        var zipFiles = Directory.GetFiles(_pluginsDirectory, "*.zip");
        if (zipFiles.Length == 0) return;

        _logger.LogInformation("Found {Count} zip file(s) in plugins directory", zipFiles.Length);

        foreach (var zipPath in zipFiles)
        {
            try
            {
                ExtractZipToDirectory(zipPath);

                // Delete the zip after successful extraction
                File.Delete(zipPath);
                _logger.LogDebug("Deleted extracted zip: {Path}", Path.GetFileName(zipPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract plugin zip: {Path}", Path.GetFileName(zipPath));
            }
        }
    }

    /// <summary>
    /// Extracts a zip archive to the plugins directory and returns the path to the
    /// extracted plugin directory (containing <c>plugin.json</c>).
    ///
    /// Handles two zip layouts:
    /// <list type="bullet">
    ///   <item><b>Flat</b>: <c>plugin.json</c> at archive root → extracted to <c>{plugins}/{ZipName}/</c></item>
    ///   <item><b>Nested</b>: single top-level directory containing <c>plugin.json</c> → extracted as-is</item>
    /// </list>
    ///
    /// If the target directory already exists, it is replaced (upgrade scenario).
    /// </summary>
    /// <returns>Full path to the extracted plugin directory.</returns>
    private string ExtractZipToDirectory(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count == 0)
            throw new InvalidOperationException($"Plugin zip is empty: {Path.GetFileName(zipPath)}");

        // Determine layout: flat (plugin.json at root) or nested (single subdir)
        var hasRootManifest = archive.Entries
            .Any(e => e.FullName.Equals("plugin.json", StringComparison.OrdinalIgnoreCase));

        string targetDir;

        if (hasRootManifest)
        {
            // Flat layout: read plugin name from manifest to determine target directory
            var manifestEntry = archive.Entries
                .First(e => e.FullName.Equals("plugin.json", StringComparison.OrdinalIgnoreCase));
            var pluginName = ReadPluginNameFromEntry(manifestEntry)
                             ?? Path.GetFileNameWithoutExtension(zipPath);
            ValidateDirectoryName(pluginName, "plugin name from manifest");
            targetDir = Path.Combine(_pluginsDirectory, pluginName);
            ValidateTargetWithinPluginsRoot(targetDir);
        }
        else
        {
            // Nested layout: check for single top-level directory containing plugin.json
            var topLevelDirs = archive.Entries
                .Select(e => e.FullName.Split('/')[0])
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (topLevelDirs.Count != 1)
                throw new InvalidOperationException(
                    $"Plugin zip '{Path.GetFileName(zipPath)}' must contain plugin.json at root " +
                    $"or a single top-level directory. Found {topLevelDirs.Count} top-level entries.");

            var innerDir = topLevelDirs[0];
            ValidateDirectoryName(innerDir, "top-level directory name in zip");

            var nestedManifest = archive.Entries
                .Any(e => e.FullName.Equals($"{innerDir}/plugin.json", StringComparison.OrdinalIgnoreCase));

            if (!nestedManifest)
                throw new InvalidOperationException(
                    $"Plugin zip '{Path.GetFileName(zipPath)}' directory '{innerDir}' does not contain plugin.json");

            targetDir = Path.Combine(_pluginsDirectory, innerDir);
            ValidateTargetWithinPluginsRoot(targetDir);
        }

        // Remove existing directory if present (upgrade)
        if (Directory.Exists(targetDir))
        {
            _logger.LogInformation("Replacing existing plugin directory: {Dir}", Path.GetFileName(targetDir));
            Directory.Delete(targetDir, recursive: true);
        }

        // Extract with zip-slip protection
        if (hasRootManifest)
        {
            // Flat: extract all entries into targetDir
            try
            {
                ExtractZipSafely(archive, targetDir);
            }
            catch
            {
                // Clean up partially extracted files
                try { Directory.Delete(targetDir, recursive: true); } catch { /* best effort cleanup */ }
                throw;
            }
        }
        else
        {
            // Nested: extract to plugins dir (the single subdir becomes targetDir)
            try
            {
                ExtractZipSafely(archive, _pluginsDirectory);
            }
            catch
            {
                // Clean up partially extracted files
                try { Directory.Delete(targetDir, recursive: true); } catch { /* best effort cleanup */ }
                throw;
            }
        }

        _logger.LogInformation("Extracted plugin zip '{Zip}' → {Dir}",
            Path.GetFileName(zipPath), Path.GetFileName(targetDir));

        // Validate that plugin.json exists in the extracted directory
        var manifestPath = Path.Combine(targetDir, "plugin.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException(
                $"Extracted plugin directory does not contain plugin.json: {targetDir}");

        return targetDir;
    }

    /// <summary>
    /// Validates that a directory name (plugin name or zip inner directory) does not
    /// contain path traversal sequences or path separators. Only alphanumeric characters,
    /// dots, dashes, and underscores are allowed.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the name contains invalid characters that could enable path traversal.
    /// </exception>
    private static void ValidateDirectoryName(string name, string context)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"Invalid {context}: name is empty or whitespace");

        if (!Regex.IsMatch(name, @"^[a-zA-Z0-9._-]+$"))
            throw new InvalidOperationException(
                $"Invalid {context}: '{name}'. Only alphanumeric characters, dots, dashes, and underscores are allowed.");
    }

    /// <summary>
    /// Validates that the resolved target directory is within the plugins root directory,
    /// preventing path traversal attacks where a malicious plugin name or zip structure
    /// could cause files to be written outside the plugins directory.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the target directory resolves to a path outside the plugins root.
    /// </exception>
    private void ValidateTargetWithinPluginsRoot(string targetDir)
    {
        var fullTarget = Path.GetFullPath(targetDir);
        var fullPlugins = Path.GetFullPath(_pluginsDirectory);
        if (!fullPlugins.EndsWith(Path.DirectorySeparatorChar.ToString()))
            fullPlugins += Path.DirectorySeparatorChar;

        if (!fullTarget.StartsWith(fullPlugins, StringComparison.Ordinal)
            && !fullTarget.Equals(fullPlugins.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Plugin target directory escapes plugins root: {targetDir}");
        }
    }

    /// <summary>
    /// Extracts a zip archive to the target directory with zip-slip protection.
    /// Validates that all entries resolve to paths within the target directory,
    /// preventing path traversal attacks via entries like <c>../../etc/passwd</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if any zip entry would extract to a path outside the target directory.
    /// </exception>
    private static void ExtractZipSafely(ZipArchive archive, string targetDir)
    {
        var fullTargetDir = Path.GetFullPath(targetDir);
        if (!fullTargetDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            fullTargetDir += Path.DirectorySeparatorChar;

        Directory.CreateDirectory(fullTargetDir);

        foreach (var entry in archive.Entries)
        {
            // Normalize backslashes to forward slashes for cross-platform safety.
            // On Linux, backslash is a valid filename char, so "..\..\evil" would not be
            // treated as traversal by Path.GetFullPath — normalize first.
            var sanitizedName = entry.FullName.Replace('\\', '/');

            // Skip directory entries (they have no Name, only FullName ending with /)
            if (string.IsNullOrEmpty(entry.Name))
            {
                var dirPath = Path.GetFullPath(Path.Combine(fullTargetDir, sanitizedName));
                if (!dirPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Zip entry '{entry.FullName}' would extract outside the target directory (zip slip attack)");
                Directory.CreateDirectory(dirPath);

                // Reject directory symlinks
                var dirInfo = new DirectoryInfo(dirPath);
                if (dirInfo.LinkTarget != null)
                {
                    Directory.Delete(dirPath);
                    throw new InvalidOperationException(
                        $"Zip entry '{entry.FullName}' is a symbolic link directory, which is not allowed for security reasons");
                }

                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(fullTargetDir, sanitizedName));
            if (!destinationPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Zip entry '{entry.FullName}' would extract outside the target directory (zip slip attack)");

            // Ensure parent directory exists
            var parentDir = Path.GetDirectoryName(destinationPath);
            if (parentDir != null)
                Directory.CreateDirectory(parentDir);

            entry.ExtractToFile(destinationPath, overwrite: true);

            // Reject symlinks created during extraction (defense-in-depth)
            var fileInfo = new FileInfo(destinationPath);
            if (fileInfo.LinkTarget != null)
            {
                File.Delete(destinationPath);
                throw new InvalidOperationException(
                    $"Zip entry '{entry.FullName}' is a symbolic link, which is not allowed for security reasons");
            }
        }
    }

    /// <summary>
    /// Reads the "name" field from a plugin.json zip entry without full deserialization.
    /// Returns null if the name cannot be read.
    /// </summary>
    private static string? ReadPluginNameFromEntry(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            var manifest = JsonSerializer.Deserialize<PluginManifestFile>(stream, ManifestJsonOptions);
            return manifest?.Name;
        }
        catch
        {
            return null;
        }
    }

    private void OnPluginDirectoryCreated(object sender, FileSystemEventArgs e)
    {
        // Delay to allow file writes to complete (1s is more reliable than 500ms
        // for large plugin archives being copied).
        // Use TaskContinuationOptions.ExecuteSynchronously to ensure exceptions are
        // observed on the continuation task, not swallowed as unobserved.
        Task.Delay(1000).ContinueWith(_ =>
        {
            try
            {
                // Handle .zip file drops
                if (e.FullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(e.FullPath))
                    {
                        _logger.LogError("Plugin zip file disappeared before loading: {Path}", e.Name);
                        return;
                    }

                    // Check file is not still being written (file stability check)
                    try
                    {
                        using var fs = File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    }
                    catch (IOException ioEx)
                    {
                        _logger.LogError(ioEx, "File still being written, skipping auto-load: {Path}", e.Name);
                        return;
                    }

                    _logger.LogInformation("Auto-loading new plugin zip: {Path}", e.Name);
                    LoadPluginFromZip(e.FullPath);

                    // Delete the zip after successful extraction + load
                    try { File.Delete(e.FullPath); }
                    catch (Exception deleteEx)
                    {
                        _logger.LogError(deleteEx, "Failed to delete plugin zip after extraction: {Path}", e.Name);
                    }
                    return;
                }

                // Handle directory drops
                var manifestPath = Path.Combine(e.FullPath, "plugin.json");
                if (!File.Exists(manifestPath))
                {
                    _logger.LogError("Plugin directory has no plugin.json, skipping: {Path}", e.Name);
                    return;
                }

                // Check manifest file is not still being written
                try
                {
                    using var fs = File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException ioEx)
                {
                    _logger.LogError(ioEx, "Manifest file still being written, skipping auto-load: {Path}", e.Name);
                    return;
                }

                var manifest = LoadManifestFile(manifestPath);
                if (!manifest.AutoLoad) return;

                lock (_lock)
                {
                    if (_loadedPlugins.ContainsKey(manifest.Name)) return;
                }

                // Pre-flight dependency check for hot-loaded plugins
                try
                {
                    ValidateManifestDependencies(manifest);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Cannot auto-load plugin '{Name}'", manifest.Name);
                    return;
                }

                _logger.LogInformation("Auto-loading new plugin directory: {Path}", e.Name);
                LoadPluginFromDirectory(e.FullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-load plugin from {Path}", e.FullPath);
            }
        }, TaskScheduler.Default);
    }
}
