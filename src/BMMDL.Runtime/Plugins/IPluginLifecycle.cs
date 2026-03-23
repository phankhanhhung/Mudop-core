namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Hooks for plugin install/enable/disable/uninstall.
/// Allows plugins to perform setup/teardown beyond schema migrations.
/// Default implementations are no-ops, so plugins only override what they need.
/// </summary>
public interface IPluginLifecycle : IPlatformFeature
{
    /// <summary>
    /// Called after the plugin is installed and migrations have run.
    /// </summary>
    Task OnInstalledAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called when the plugin transitions to enabled state.
    /// </summary>
    Task OnEnabledAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called when the plugin transitions to disabled state.
    /// </summary>
    Task OnDisabledAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called before the plugin is uninstalled and down-migrations run.
    /// </summary>
    Task OnUninstalledAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;
}
