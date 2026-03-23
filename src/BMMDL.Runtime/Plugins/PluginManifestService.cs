namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Aggregates plugin metadata into a manifest consumed by the frontend admin UI.
/// Called by PluginController GET /api/plugins/manifest.
///
/// Iterates all registered features from <see cref="PlatformFeatureRegistry"/>,
/// merges persisted state from <see cref="IPluginManager"/>, and produces a
/// <see cref="PluginManifest"/> with capabilities, menu items, pages, and settings.
/// </summary>
public class PluginManifestService
{
    private readonly PlatformFeatureRegistry _registry;
    private readonly IPluginManager _pluginManager;

    public PluginManifestService(PlatformFeatureRegistry registry, IPluginManager pluginManager)
    {
        _registry = registry;
        _pluginManager = pluginManager;
    }

    /// <summary>
    /// Build the complete plugin manifest for the frontend.
    /// Combines registry metadata (capabilities, menu, pages, settings schema)
    /// with persisted state (status, settings values) for each registered feature.
    /// </summary>
    /// <summary>
    /// Alias for <see cref="BuildManifestAsync"/> — used by PluginController.
    /// </summary>
    public Task<PluginManifest> GetManifestAsync(CancellationToken ct)
        => BuildManifestAsync(ct);

    public async Task<PluginManifest> BuildManifestAsync(CancellationToken ct)
    {
        var states = await _pluginManager.GetAllPluginStatesAsync(ct);
        var stateMap = states.ToDictionary(s => s.Name);

        var plugins = new List<PluginManifestEntry>();

        foreach (var feature in _registry.AllFeatures)
        {
            var state = stateMap.GetValueOrDefault(feature.Name);
            var status = state?.Status.ToString().ToLowerInvariant() ?? "available";

            var entry = new PluginManifestEntry
            {
                Name = feature.Name,
                Status = status,
                DependsOn = feature.DependsOn.ToList(),
                Capabilities = GetCapabilities(feature),
                IsGloballyActive = _registry.IsFeatureGloballyActive(feature.Name),
                MenuItems = GetMenuItems(feature, status),
                Pages = GetPages(feature, status),
                Settings = GetSettings(feature, state),
                BmmdlModules = GetBmmdlModules(feature, _pluginManager),
            };

            plugins.Add(entry);
        }

        return new PluginManifest { Plugins = plugins };
    }

    /// <summary>
    /// Introspects which capability interfaces a feature implements.
    /// Returns a list of capability names for the frontend to display.
    /// </summary>
    private static List<string> GetCapabilities(IPlatformFeature feature)
    {
        var caps = new List<string>();
        if (feature is IFeatureMetadataContributor) caps.Add("metadata");
        if (feature is IFeatureQueryFilter) caps.Add("queryFilter");
        if (feature is IFeatureInsertContributor) caps.Add("insertContributor");
        if (feature is IFeatureUpdateContributor) caps.Add("updateContributor");
        if (feature is IFeatureDeleteStrategy) caps.Add("deleteStrategy");
        if (feature is IPlatformEntityProvider) caps.Add("platformEntities");
        if (feature is IAdminPageProvider) caps.Add("adminPages");
        if (feature is IMenuContributor) caps.Add("menuItems");
        if (feature is ISettingsProvider) caps.Add("settings");
        if (feature is IMigrationProvider) caps.Add("migrations");
        if (feature is IBmmdlModuleProvider) caps.Add("bmmdlModules");
        if (feature is IGlobalFeature) caps.Add("globalFeature");
        return caps;
    }

    /// <summary>
    /// Returns menu items only for enabled plugins that implement <see cref="IMenuContributor"/>.
    /// </summary>
    private static List<PluginMenuItem> GetMenuItems(IPlatformFeature feature, string status)
    {
        if (status != "enabled" || feature is not IMenuContributor mc) return [];
        return mc.GetMenuItems().ToList();
    }

    /// <summary>
    /// Returns page definitions only for enabled plugins that implement <see cref="IAdminPageProvider"/>.
    /// </summary>
    private static List<PluginPageDefinition> GetPages(IPlatformFeature feature, string status)
    {
        if (status != "enabled" || feature is not IAdminPageProvider pp) return [];
        return pp.GetPages().ToList();
    }

    /// <summary>
    /// Returns settings schema and current values for plugins that implement <see cref="ISettingsProvider"/>.
    /// Returns null for plugins without configurable settings.
    /// </summary>
    private static PluginManifestSettings? GetSettings(IPlatformFeature feature, PluginStateRecord? state)
    {
        if (feature is not ISettingsProvider sp) return null;
        return new PluginManifestSettings
        {
            Schema = sp.GetSettingsSchema(),
            Values = state?.Settings ?? new()
        };
    }

    /// <summary>
    /// Returns BMMDL module info from both code-based IBmmdlModuleProvider
    /// and external plugin manifest (plugin.json bmmdlModules).
    /// </summary>
    private static List<PluginBmmdlModuleInfo> GetBmmdlModules(IPlatformFeature feature, IPluginManager pluginManager)
    {
        var modules = new List<PluginBmmdlModuleInfo>();

        // From code-based IBmmdlModuleProvider
        if (feature is IBmmdlModuleProvider provider)
        {
            modules.AddRange(provider.GetModules().Select(m => new PluginBmmdlModuleInfo
            {
                Name = m.ModuleName,
                AutoInstall = true,
                InitSchema = m.InitSchema
            }));
        }

        // From external plugin manifest (plugin.json bmmdlModules)
        var externalPlugins = pluginManager.GetLoadedExternalPlugins();
        if (externalPlugins.TryGetValue(feature.Name, out var descriptor))
        {
            foreach (var entry in descriptor.Manifest.BmmdlModules)
            {
                if (!modules.Any(m => m.Name == entry.Name))
                {
                    modules.Add(new PluginBmmdlModuleInfo
                    {
                        Name = entry.Name,
                        AutoInstall = entry.AutoInstall,
                        InitSchema = entry.InitSchema
                    });
                }
            }
        }

        return modules;
    }
}
