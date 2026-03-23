namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Configuration options for the plugin bootstrap process.
/// Bound from <c>Plugins:Bootstrap</c> in appsettings.json.
///
/// Example configuration:
/// <code>
/// {
///   "Plugins": {
///     "Bootstrap": {
///       "Exclude": ["EventOutbox", "Webhooks", "AuditLogging", "Collaboration", "Reporting", "UserPreferences"]
///     }
///   }
/// }
/// </code>
/// </summary>
public sealed class PluginBootstrapOptions
{
    /// <summary>
    /// List of built-in plugin names to exclude from automatic bootstrap.
    /// Excluded plugins will NOT be auto-installed or auto-enabled on startup.
    ///
    /// Dependency cascade: if plugin A is excluded and plugin B depends on A,
    /// plugin B is automatically excluded too (with a warning log).
    ///
    /// Excluded plugins can still be manually installed/enabled later via the admin API.
    /// They remain registered in the <see cref="PlatformFeatureRegistry"/> — only the
    /// automatic lifecycle (install + enable) is skipped.
    /// </summary>
    public List<string> Exclude { get; set; } = [];
}
