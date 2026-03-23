namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Provides schema migrations for plugin-owned tables.
/// Migrations run during plugin install/upgrade.
/// </summary>
public interface IMigrationProvider : IPlatformFeature
{
    /// <summary>
    /// Returns the ordered list of migrations for this plugin.
    /// Versions must be sequential starting from 1.
    /// </summary>
    IReadOnlyList<PluginMigration> GetMigrations();
}
