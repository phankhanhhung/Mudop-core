namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Optional interface for plugins that bundle BMMDL module definitions.
/// When implemented, the plugin's BMMDL modules can be compiled and installed
/// into the Registry system during plugin installation or on demand.
/// </summary>
public interface IBmmdlModuleProvider
{
    /// <summary>
    /// Returns the BMMDL module definitions provided by this plugin.
    /// Each module will be compiled and installed into the Registry.
    /// </summary>
    IReadOnlyList<BmmdlModuleDefinition> GetModules();
}

/// <summary>
/// Describes a BMMDL module bundled with a plugin.
/// </summary>
/// <param name="ModuleName">Module name for identification and error reporting.</param>
/// <param name="BmmdlSource">Raw BMMDL source code.</param>
/// <param name="InitSchema">Whether to initialize database tables after compilation.</param>
public record BmmdlModuleDefinition(
    string ModuleName,
    string BmmdlSource,
    bool InitSchema = true
);
