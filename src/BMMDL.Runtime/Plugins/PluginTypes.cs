namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Defines an admin UI page contributed by a plugin.
/// The backend serves these as part of the plugin manifest; the Vue frontend renders them.
/// </summary>
/// <param name="Route">Route path, e.g., "/admin/tenants".</param>
/// <param name="Title">Page title, e.g., "Tenant Management".</param>
/// <param name="Component">Vue component name, e.g., "PluginTenantList".</param>
/// <param name="Icon">Optional Lucide icon name.</param>
/// <param name="ParentRoute">Optional parent route for nested pages.</param>
/// <param name="Meta">Optional route metadata.</param>
public record PluginPageDefinition(
    string Route,
    string Title,
    string Component,
    string? Icon = null,
    string? ParentRoute = null,
    Dictionary<string, object>? Meta = null);

/// <summary>
/// Defines a sidebar menu item contributed by a plugin.
/// </summary>
/// <param name="Label">Display label, e.g., "Tenants".</param>
/// <param name="Route">Navigation route, e.g., "/admin/tenants".</param>
/// <param name="Icon">Lucide icon name.</param>
/// <param name="Section">Menu section: "main", "admin", or "tools".</param>
/// <param name="Order">Sort order within section (lower = first).</param>
/// <param name="Badge">Optional badge text displayed on the menu item.</param>
/// <param name="RequiredPermission">Optional permission required to show this item.</param>
public record PluginMenuItem(
    string Label,
    string Route,
    string Icon,
    string Section,
    int Order = 100,
    string? Badge = null,
    string? RequiredPermission = null);

/// <summary>
/// Defines the settings schema for a plugin.
/// Admin UI renders a settings form based on this schema.
/// </summary>
/// <param name="GroupLabel">Display label for the settings group, e.g., "Multi-Tenancy Settings".</param>
/// <param name="Settings">The list of configurable settings.</param>
public record PluginSettingsSchema(
    string GroupLabel,
    IReadOnlyList<PluginSetting> Settings);

/// <summary>
/// Defines a single configurable setting for a plugin.
/// </summary>
/// <param name="Key">Setting key, e.g., "maxTenants".</param>
/// <param name="Label">Display label, e.g., "Maximum Tenants".</param>
/// <param name="Type">Setting type: "boolean", "integer", "string", or "select".</param>
/// <param name="DefaultValue">Default value for the setting.</param>
/// <param name="Required">Whether the setting is required.</param>
/// <param name="Description">Optional description shown as help text.</param>
/// <param name="Options">Options for "select" type settings.</param>
public record PluginSetting(
    string Key,
    string Label,
    string Type,
    object? DefaultValue,
    bool Required = false,
    string? Description = null,
    string[]? Options = null);

/// <summary>
/// Defines a schema migration for a plugin-owned table.
/// </summary>
/// <param name="Version">Sequential version number starting from 1.</param>
/// <param name="Description">Human-readable description of the migration.</param>
/// <param name="UpSql">SQL to apply the migration.</param>
/// <param name="DownSql">SQL to revert the migration.</param>
public record PluginMigration(
    int Version,
    string Description,
    string UpSql,
    string DownSql);

/// <summary>
/// Context passed to <see cref="IPluginLifecycle"/> hooks.
/// Provides access to DI services and the plugin's current settings.
/// </summary>
public class PluginContext
{
    /// <summary>
    /// The application's service provider for resolving dependencies.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// The plugin's current settings. Keys match <see cref="PluginSetting.Key"/>.
    /// </summary>
    public Dictionary<string, object?> Settings { get; init; } = new();
}
