using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using BMMDL.Runtime.Plugins.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Central service that manages plugin lifecycle: install, enable, disable, uninstall.
/// Reads/writes <c>core.plugin_state</c> and <c>core.plugin_migration_history</c>.
///
/// Also integrates with <see cref="PluginDirectoryLoader"/> for runtime DLL loading:
/// external plugins can be loaded from disk, their features discovered and added to
/// the registry, then managed through the same lifecycle as built-in features.
/// </summary>
public sealed class PluginManager : IPluginManager
{
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly PlatformFeatureRegistry _registry;
    private readonly PluginDirectoryLoader? _loader;
    private readonly IServiceProvider _services;
    private readonly ILogger<PluginManager> _logger;
    private readonly IRegistryClient? _registryClient;
    private readonly ConcurrentDictionary<string, PluginStatus> _stateCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public PluginManager(
        ITenantConnectionFactory connectionFactory,
        PlatformFeatureRegistry registry,
        IServiceProvider services,
        ILogger<PluginManager> logger,
        PluginDirectoryLoader? loader = null,
        IRegistryClient? registryClient = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loader = loader; // null when dynamic loading is not configured
        _registryClient = registryClient;
    }

    /// <summary>
    /// Ensures the <c>core.plugin_state</c> and <c>core.plugin_migration_history</c> tables exist.
    /// Safe to call multiple times (uses IF NOT EXISTS).
    /// </summary>
    public async Task EnsurePluginTablesAsync(CancellationToken ct = default)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS core;

            CREATE TABLE IF NOT EXISTS core.plugin_state (
                name VARCHAR(100) PRIMARY KEY,
                version INTEGER NOT NULL DEFAULT 1,
                status VARCHAR(20) NOT NULL DEFAULT 'installed',
                installed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                enabled_at TIMESTAMPTZ,
                settings_json JSONB DEFAULT '{}',
                CHECK (status IN ('installed', 'enabled', 'disabled'))
            );

            CREATE TABLE IF NOT EXISTS core.plugin_migration_history (
                plugin_name VARCHAR(100) NOT NULL,
                version INTEGER NOT NULL,
                description VARCHAR(500),
                applied_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                checksum VARCHAR(64),
                PRIMARY KEY (plugin_name, version)
            );

            CREATE TABLE IF NOT EXISTS core.plugin_staging (
                id SERIAL PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                version VARCHAR(50) NOT NULL DEFAULT '1.0.0',
                description TEXT,
                author VARCHAR(255),
                file_hash VARCHAR(64) NOT NULL,
                file_size BIGINT NOT NULL DEFAULT 0,
                file_name VARCHAR(500) NOT NULL,
                staging_path VARCHAR(1000) NOT NULL,
                validation_status VARCHAR(20) NOT NULL DEFAULT 'pending',
                uploaded_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                approved_at TIMESTAMPTZ,
                validation_results JSONB DEFAULT '[]',
                CHECK (validation_status IN ('pending', 'valid', 'invalid', 'approved', 'rejected'))
            );

            CREATE INDEX IF NOT EXISTS idx_plugin_staging_hash ON core.plugin_staging(file_hash);
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Plugin state tables ensured");
    }

    /// <summary>
    /// Returns all plugin state rows from <c>core.plugin_state</c>.
    /// </summary>
    public async Task<IReadOnlyList<PluginStateRecord>> GetAllPluginStatesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT name, version, status, installed_at, enabled_at, settings_json
            FROM core.plugin_state
            ORDER BY name
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<PluginStateRecord>();
        while (await reader.ReadAsync(ct))
        {
            var state = ReadPluginState(reader);
            results.Add(state);
            _stateCache[state.Name] = state.Status;
        }

        return results;
    }

    /// <summary>
    /// Returns the state of a single plugin, or null if not installed.
    /// </summary>
    public async Task<PluginStateRecord?> GetPluginStateAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        const string sql = """
            SELECT name, version, status, installed_at, enabled_at, settings_json
            FROM core.plugin_state
            WHERE name = @name
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("@name", name));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return null;

        var state = ReadPluginState(reader);
        _stateCache[state.Name] = state.Status;
        return state;
    }

    /// <summary>
    /// Installs a plugin: runs pending migrations, inserts state row (status=installed),
    /// and calls <see cref="IPluginLifecycle.OnInstalledAsync"/>.
    /// </summary>
    public async Task<PluginStateRecord> InstallPluginAsync(
        string name,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        ValidatePluginName(name);

        // Check if already installed
        var existing = await GetPluginStateAsync(name, ct);
        if (existing is not null)
            throw new InvalidOperationException($"Plugin '{name}' is already installed (status: {existing.Status})");

        // Verify plugin exists in registry (built-in or externally loaded)
        var feature = _registry.GetFeature(name);
        if (feature is null)
            throw new InvalidOperationException($"Plugin '{name}' is not a known feature. Register it first via the platform feature registry.");

        _logger.LogInformation("Installing plugin '{PluginName}'", name);

        // Run migrations first (idempotent with IF NOT EXISTS)
        await RunPendingMigrationsAsync(name, ct);

        // Call lifecycle hook BEFORE persisting state — if this fails, no state row is written
        // so next startup will retry from scratch
        var lifecycle = FindFeature<IPluginLifecycle>(name);
        if (lifecycle is not null)
        {
            var ctx = new PluginContext
            {
                Services = services,
                Settings = new Dictionary<string, object?>()
            };
            await lifecycle.OnInstalledAsync(ctx, ct);
        }

        // Auto-install BMMDL modules — if Registry API is unavailable, log warning and continue.
        // Plugins with IPlatformEntityProvider will still function via MetaModelCache fallback.
        var moduleResults = await InstallPluginModulesAsync(name, force: false, ct);
        var failedModules = moduleResults.Where(r => !r.Success).ToList();
        if (failedModules.Count > 0)
        {
            var errorDetails = string.Join("; ", failedModules.SelectMany(r => r.Errors));
            _logger.LogWarning(
                "Plugin '{PluginName}' BMMDL module installation had failures (non-blocking): {Errors}. " +
                "Plugin will rely on IPlatformEntityProvider fallback for entity definitions.",
                name, errorDetails);
        }

        // ONLY insert state row after migrations, lifecycle hook, and module installation succeed.
        // Wrap lifecycle hook + state INSERT in a transaction so that if the INSERT fails,
        // we don't leave behind orphaned plugin resources with no state row.
        const string sql = """
            INSERT INTO core.plugin_state (name, version, status, installed_at, settings_json)
            VALUES (@name, 1, 'installed', now(), '{}')
            RETURNING name, version, status, installed_at, enabled_at, settings_json
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        PluginStateRecord state;
        try
        {
            await using var cmd = new NpgsqlCommand(sql, connection, transaction);
            cmd.Parameters.Add(new NpgsqlParameter("@name", name));

            // Use a block scope for the reader so it's disposed before CommitAsync
            {
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                if (!await reader.ReadAsync(ct))
                    throw new InvalidOperationException($"Failed to insert plugin state for '{name}'");

                state = ReadPluginState(reader);
            }

            await transaction.CommitAsync(ct);
        }
        catch (NpgsqlException ex) when (ex.SqlState == "23505") // unique_violation
        {
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException($"Plugin '{name}' is already installed (concurrent install detected)", ex);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        _stateCache[name] = state.Status;

        _logger.LogInformation("Plugin '{PluginName}' installed successfully", name);
        return state;
    }

    /// <summary>
    /// Enables an installed plugin. Validates that all dependencies are also enabled.
    /// Updates status to enabled and calls <see cref="IPluginLifecycle.OnEnabledAsync"/>.
    /// </summary>
    public async Task<PluginStateRecord> EnablePluginAsync(
        string name,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        ValidatePluginName(name);

        var existing = await GetPluginStateAsync(name, ct)
            ?? throw new InvalidOperationException($"Plugin '{name}' is not installed");

        if (existing.Status == PluginStatus.Enabled)
            return existing;

        // Validate dependencies: all deps must be enabled
        var feature = FindFeature<IPlatformFeature>(name);
        if (feature is not null)
        {
            foreach (var dep in feature.DependsOn)
            {
                var depState = await GetPluginStateAsync(dep, ct);
                if (depState is null || depState.Status != PluginStatus.Enabled)
                    throw new InvalidOperationException(
                        $"Cannot enable plugin '{name}': dependency '{dep}' is not enabled");
            }
        }

        _logger.LogInformation("Enabling plugin '{PluginName}'", name);

        // Call lifecycle hook BEFORE updating DB — if hook fails, DB stays unchanged
        var lifecycle = FindFeature<IPluginLifecycle>(name);
        if (lifecycle is not null)
        {
            var ctx = new PluginContext
            {
                Services = services,
                Settings = existing.Settings
            };
            await lifecycle.OnEnabledAsync(ctx, ct);
        }

        // Activate global mode BEFORE DB update — if activation fails, DB still shows
        // non-enabled so the state is consistent on retry
        ActivateGlobalFeatureIfSupported(name);

        // Apply RLS policies when MultiTenancy plugin is enabled (defense-in-depth)
        if (name.Equals("TenantIsolation", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyRlsPoliciesAsync(services, ct);
        }

        const string sql = """
            UPDATE core.plugin_state
            SET status = 'enabled', enabled_at = now()
            WHERE name = @name
            RETURNING name, version, status, installed_at, enabled_at, settings_json
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("@name", name));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException($"Failed to update plugin state for '{name}'");

        var state = ReadPluginState(reader);

        _stateCache[name] = state.Status;

        _logger.LogInformation("Plugin '{PluginName}' enabled successfully", name);
        return state;
    }

    /// <summary>
    /// Disables a plugin. Validates that no other enabled plugin depends on this one.
    /// Updates status to disabled and calls <see cref="IPluginLifecycle.OnDisabledAsync"/>.
    /// </summary>
    public async Task<PluginStateRecord> DisablePluginAsync(
        string name,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        ValidatePluginName(name);

        var existing = await GetPluginStateAsync(name, ct)
            ?? throw new InvalidOperationException($"Plugin '{name}' is not installed");

        if (existing.Status == PluginStatus.Disabled)
            return existing;

        if (existing.Status != PluginStatus.Enabled)
            throw new InvalidOperationException(
                $"Cannot disable plugin '{name}': plugin must be enabled first (current status: {existing.Status})");

        // Validate: no enabled plugin depends on this one
        await ValidateNoDependentsEnabledAsync(name, ct);

        _logger.LogInformation("Disabling plugin '{PluginName}'", name);

        // Call lifecycle hook BEFORE updating DB — if hook fails, DB stays unchanged
        var lifecycle = FindFeature<IPluginLifecycle>(name);
        if (lifecycle is not null)
        {
            var ctx = new PluginContext
            {
                Services = services,
                Settings = existing.Settings
            };
            await lifecycle.OnDisabledAsync(ctx, ct);
        }

        // Drop RLS policies when MultiTenancy plugin is disabled
        if (name.Equals("TenantIsolation", StringComparison.OrdinalIgnoreCase))
        {
            await DropRlsPoliciesAsync(services, ct);
        }

        // Deactivate global mode BEFORE DB update — if deactivation fails, DB still
        // shows enabled so the state is consistent on retry
        _registry.DeactivateGlobalFeature(name);

        const string sql = """
            UPDATE core.plugin_state
            SET status = 'disabled', enabled_at = NULL
            WHERE name = @name
            RETURNING name, version, status, installed_at, enabled_at, settings_json
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("@name", name));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException($"Failed to update plugin state for '{name}'");

        var state = ReadPluginState(reader);

        _stateCache[name] = state.Status;

        _logger.LogInformation("Plugin '{PluginName}' disabled successfully", name);
        return state;
    }

    /// <summary>
    /// Uninstalls a plugin. Must be disabled first. Runs down-migrations in reverse order,
    /// deletes the state row, and calls <see cref="IPluginLifecycle.OnUninstalledAsync"/>.
    /// </summary>
    public async Task UninstallPluginAsync(
        string name,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        ValidatePluginName(name);

        var existing = await GetPluginStateAsync(name, ct)
            ?? throw new InvalidOperationException($"Plugin '{name}' is not installed");

        if (existing.Status == PluginStatus.Enabled)
            throw new InvalidOperationException(
                $"Cannot uninstall plugin '{name}': it must be disabled first");

        _logger.LogInformation("Uninstalling plugin '{PluginName}'", name);

        // Run down-migrations BEFORE lifecycle hook — migrations drop tables,
        // and the hook should run after tables are gone for final cleanup
        var migrationProvider = FindFeature<IMigrationProvider>(name);
        if (migrationProvider is not null)
        {
            var appliedMigrations = await GetAppliedMigrationsAsync(name, ct);
            var allMigrations = migrationProvider.GetMigrations();

            // Run DownSql for applied migrations in reverse version order
            var migrationsToRevert = allMigrations
                .Where(m => appliedMigrations.Contains(m.Version))
                .OrderByDescending(m => m.Version)
                .ToList();

            foreach (var migration in migrationsToRevert)
            {
                _logger.LogDebug(
                    "Reverting migration v{Version} for plugin '{PluginName}': {Description}",
                    migration.Version, name, migration.Description);

                await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                try
                {
                    await using var migCmd = new NpgsqlCommand(migration.DownSql, connection, transaction);
                    await migCmd.ExecuteNonQueryAsync(ct);

                    // Remove from migration history
                    await using var delCmd = new NpgsqlCommand(
                        "DELETE FROM core.plugin_migration_history WHERE plugin_name = @name AND version = @version",
                        connection, transaction);
                    delCmd.Parameters.Add(new NpgsqlParameter("@name", name));
                    delCmd.Parameters.Add(new NpgsqlParameter("@version", migration.Version));
                    await delCmd.ExecuteNonQueryAsync(ct);

                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    try { await transaction.RollbackAsync(ct); }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogWarning(rollbackEx, "Rollback failed for down-migration v{Version}", migration.Version);
                    }
                    throw;
                }
            }
        }

        // Call lifecycle hook AFTER down-migrations — tables are already dropped
        var lifecycle = FindFeature<IPluginLifecycle>(name);
        if (lifecycle is not null)
        {
            var ctx = new PluginContext
            {
                Services = services,
                Settings = existing.Settings
            };
            await lifecycle.OnUninstalledAsync(ctx, ct);
        }

        // Delete state row
        await using var conn = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var deleteCmd = new NpgsqlCommand(
            "DELETE FROM core.plugin_state WHERE name = @name", conn);
        deleteCmd.Parameters.Add(new NpgsqlParameter("@name", name));
        await deleteCmd.ExecuteNonQueryAsync(ct);

        _stateCache.TryRemove(name, out _);
        _logger.LogInformation("Plugin '{PluginName}' uninstalled successfully", name);
    }

    /// <summary>
    /// Updates the settings for an installed plugin.
    /// Validates settings via <see cref="ISettingsProvider"/> if available, then persists.
    /// </summary>
    public async Task<PluginStateRecord> UpdateSettingsAsync(
        string name,
        Dictionary<string, object?> settings,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);

        var existing = await GetPluginStateAsync(name, ct)
            ?? throw new InvalidOperationException($"Plugin '{name}' is not installed");

        var settingsJson = JsonSerializer.Serialize(settings, JsonOptions);

        const string sql = """
            UPDATE core.plugin_state
            SET settings_json = @settings::jsonb
            WHERE name = @name
            RETURNING name, version, status, installed_at, enabled_at, settings_json
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("@name", name));
        cmd.Parameters.Add(new NpgsqlParameter("@settings", settingsJson));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException($"Failed to update settings for plugin '{name}'");

        var state = ReadPluginState(reader);

        // Notify plugin AFTER settings are persisted — if DB update failed, we never reach here
        var settingsProvider = FindFeature<ISettingsProvider>(name);
        if (settingsProvider is not null)
        {
            try
            {
                await settingsProvider.OnSettingsChangedAsync(settings, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Settings notification failed for plugin '{Name}'", name);
            }
        }

        _logger.LogInformation("Settings updated for plugin '{PluginName}'", name);
        return state;
    }

    /// <summary>
    /// Runs any pending migrations for the given plugin by comparing
    /// <see cref="IMigrationProvider.GetMigrations"/> against <c>core.plugin_migration_history</c>.
    /// </summary>
    public async Task RunPendingMigrationsAsync(string pluginName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        var migrationProvider = FindFeature<IMigrationProvider>(pluginName);
        if (migrationProvider is null)
        {
            _logger.LogDebug("Plugin '{PluginName}' has no migration provider — skipping", pluginName);
            return;
        }

        var allMigrations = migrationProvider.GetMigrations();
        if (allMigrations.Count == 0)
            return;

        var applied = await GetAppliedMigrationsAsync(pluginName, ct);
        var pending = allMigrations
            .Where(m => !applied.Contains(m.Version))
            .OrderBy(m => m.Version)
            .ToList();

        if (pending.Count == 0)
        {
            _logger.LogDebug("No pending migrations for plugin '{PluginName}'", pluginName);
            return;
        }

        _logger.LogInformation(
            "Running {Count} pending migration(s) for plugin '{PluginName}'",
            pending.Count, pluginName);

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);

        foreach (var migration in pending)
        {
            _logger.LogDebug(
                "Applying migration v{Version} for plugin '{PluginName}': {Description}",
                migration.Version, pluginName, migration.Description);

            // Wrap each migration in a transaction for atomicity
            await using var transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                // Execute the UpSql
                await using var migCmd = new NpgsqlCommand(migration.UpSql, connection, transaction);
                await migCmd.ExecuteNonQueryAsync(ct);

                // Record in migration history
                var checksum = ComputeChecksum(migration.UpSql);

                const string insertSql = """
                    INSERT INTO core.plugin_migration_history (plugin_name, version, description, applied_at, checksum)
                    VALUES (@name, @version, @description, now(), @checksum)
                    """;

                await using var histCmd = new NpgsqlCommand(insertSql, connection, transaction);
                histCmd.Parameters.Add(new NpgsqlParameter("@name", pluginName));
                histCmd.Parameters.Add(new NpgsqlParameter("@version", migration.Version));
                histCmd.Parameters.Add(new NpgsqlParameter("@description", migration.Description));
                histCmd.Parameters.Add(new NpgsqlParameter("@checksum", checksum));
                await histCmd.ExecuteNonQueryAsync(ct);

                await transaction.CommitAsync(ct);
            }
            catch
            {
                try { await transaction.RollbackAsync(ct); }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Rollback failed for migration v{Version} of plugin {Name}", migration.Version, pluginName);
                }
                throw;
            }
        }

        _logger.LogInformation(
            "Completed {Count} migration(s) for plugin '{PluginName}'",
            pending.Count, pluginName);
    }

    /// <summary>
    /// Quick async check whether a plugin is currently enabled.
    /// </summary>
    public async Task<bool> IsPluginEnabledAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        const string sql = """
            SELECT status FROM core.plugin_state WHERE name = @name
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("@name", name));

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string status && status == "enabled";
    }

    // ── BMMDL Module Installation ──

    /// <summary>
    /// Compiles and installs BMMDL modules bundled with a plugin.
    /// Checks both code-based IBmmdlModuleProvider and external plugin.json manifest.
    /// </summary>
    public async Task<IReadOnlyList<ModuleInstallResult>> InstallPluginModulesAsync(
        string pluginName,
        bool force = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        if (_registryClient is null)
        {
            _logger.LogWarning(
                "Skipping BMMDL module installation for plugin '{PluginName}': Registry client not configured. " +
                "Plugin will rely on IPlatformEntityProvider fallback for entity definitions.",
                pluginName);
            return [];
        }

        var modules = CollectBmmdlModules(pluginName);
        if (modules.Count == 0)
        {
            _logger.LogDebug("Plugin '{PluginName}' has no BMMDL modules to install", pluginName);
            return [];
        }

        _logger.LogInformation(
            "Installing {Count} BMMDL module(s) for plugin '{PluginName}'",
            modules.Count, pluginName);

        var results = new List<ModuleInstallResult>();

        foreach (var module in modules)
        {
            var request = new ModuleInstallRequest
            {
                BmmdlSource = module.BmmdlSource,
                ModuleName = module.ModuleName,
                InitSchema = module.InitSchema,
                Force = force
            };

            try
            {
                var result = await _registryClient.CompileAndInstallModuleAsync(request, ct);
                results.Add(result);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "BMMDL module '{ModuleName}' installed successfully ({EntityCount} entities)",
                        module.ModuleName, result.EntityCount);
                }
                else
                {
                    _logger.LogError(
                        "BMMDL module '{ModuleName}' installation failed: {Errors}",
                        module.ModuleName, string.Join("; ", result.Errors));
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to connect to Registry API for module '{ModuleName}': {Message}",
                    module.ModuleName, ex.Message);
                results.Add(new ModuleInstallResult
                {
                    Success = false,
                    Errors = [$"Failed to connect to Registry API: {ex.Message}"]
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Collects BMMDL module definitions from both code-based providers and plugin.json manifests.
    /// </summary>
    private List<BmmdlModuleDefinition> CollectBmmdlModules(string pluginName)
    {
        var modules = new List<BmmdlModuleDefinition>();

        // 1. From code-based IBmmdlModuleProvider
        var provider = FindFeature<IBmmdlModuleProvider>(pluginName);
        if (provider is not null)
        {
            modules.AddRange(provider.GetModules());
        }

        // 2. From external plugin manifest (plugin.json bmmdlModules)
        if (_loader?.LoadedPlugins.TryGetValue(pluginName, out var descriptor) == true)
        {
            foreach (var entry in descriptor.Manifest.BmmdlModules)
            {
                // Skip if already provided by code-based provider
                if (modules.Any(m => m.ModuleName == entry.Name))
                    continue;

                // Read source file from plugin directory (with path traversal protection)
                var sourceFilePath = Path.GetFullPath(Path.Combine(descriptor.DirectoryPath, entry.SourceFile));
                var pluginDirFull = Path.GetFullPath(descriptor.DirectoryPath + Path.DirectorySeparatorChar);
                if (!sourceFilePath.StartsWith(pluginDirFull, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Path traversal detected in plugin '{PluginName}' module '{ModuleName}': {Path}",
                        pluginName, entry.Name, entry.SourceFile);
                    continue;
                }

                // Reject symlinks (defense-in-depth against symlink escape)
                var fileInfo = new FileInfo(sourceFilePath);
                if (fileInfo.LinkTarget != null)
                {
                    _logger.LogWarning(
                        "Symlink detected in plugin '{PluginName}' module '{ModuleName}': {Path} -> {Target}",
                        pluginName, entry.Name, entry.SourceFile, fileInfo.LinkTarget);
                    continue;
                }

                // Resolve symlinks and verify final target is still within plugin dir
                var resolvedPath = Path.GetFullPath(fileInfo.FullName);
                if (!resolvedPath.StartsWith(pluginDirFull, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Resolved path escapes plugin directory for '{PluginName}' module '{ModuleName}': {Path}",
                        pluginName, entry.Name, resolvedPath);
                    continue;
                }

                if (!File.Exists(sourceFilePath))
                {
                    _logger.LogWarning(
                        "BMMDL source file not found for plugin '{PluginName}' module '{ModuleName}': {Path}",
                        pluginName, entry.Name, sourceFilePath);
                    continue;
                }

                var source = File.ReadAllText(sourceFilePath);
                modules.Add(new BmmdlModuleDefinition(
                    entry.Name,
                    source,
                    entry.InitSchema));
            }
        }

        return modules;
    }

    // ── IPluginManager explicit implementations ──

    Task<PluginStateRecord> IPluginManager.InstallPluginAsync(string name, CancellationToken ct)
        => InstallPluginAsync(name, _services, ct);

    Task<PluginStateRecord> IPluginManager.EnablePluginAsync(string name, CancellationToken ct)
        => EnablePluginAsync(name, _services, ct);

    Task<PluginStateRecord> IPluginManager.DisablePluginAsync(string name, CancellationToken ct)
        => DisablePluginAsync(name, _services, ct);

    Task IPluginManager.UninstallPluginAsync(string name, CancellationToken ct)
        => UninstallPluginAsync(name, _services, ct);

    async Task<Dictionary<string, object?>> IPluginManager.UpdateSettingsAsync(
        string name, Dictionary<string, object?> settings, CancellationToken ct)
    {
        var state = await UpdateSettingsAsync(name, settings, ct);
        return state.Settings;
    }

    Task<IReadOnlyList<ModuleInstallResult>> IPluginManager.InstallPluginModulesAsync(
        string pluginName, bool force, CancellationToken ct)
        => InstallPluginModulesAsync(pluginName, force, ct);

    /// <summary>
    /// Synchronous check whether a plugin is currently enabled.
    /// For built-in features without state records, returns true (backward compat).
    /// </summary>
    public bool IsPluginEnabled(string name)
    {
        var feature = _registry.GetFeature(name);
        if (feature is null) return false;

        // Check cached state first
        if (_stateCache.TryGetValue(name, out var status))
            return status == PluginStatus.Enabled;

        // External plugins without a state record are not enabled (just loaded)
        if (_loader?.LoadedPlugins.ContainsKey(name) == true)
            return false;

        // Built-in features without explicit state management are always "enabled"
        return true;
    }

    // ── Dynamic loading (IPluginManager) ──

    /// <inheritdoc/>
    public PluginDescriptor? LoadPluginFromDirectory(string pluginDirectory)
    {
        if (_loader is null)
            throw new InvalidOperationException(
                "Dynamic plugin loading is not configured. Call AddDynamicPluginLoading() during startup.");

        return _loader.LoadPluginFromDirectory(pluginDirectory);
    }

    /// <inheritdoc/>
    public PluginDescriptor? LoadPluginFromZip(string zipPath)
    {
        if (_loader is null)
            throw new InvalidOperationException(
                "Dynamic plugin loading is not configured. Call AddDynamicPluginLoading() during startup.");

        return _loader.LoadPluginFromZip(zipPath);
    }

    /// <inheritdoc/>
    public PluginDescriptor? LoadPluginFromZipStream(Stream zipStream, string? originalFileName = null)
    {
        if (_loader is null)
            throw new InvalidOperationException(
                "Dynamic plugin loading is not configured. Call AddDynamicPluginLoading() during startup.");

        return _loader.LoadPluginFromZipStream(zipStream, originalFileName);
    }

    /// <inheritdoc/>
    public void UnloadPlugin(string pluginName)
    {
        if (_loader is null)
            throw new InvalidOperationException(
                "Dynamic plugin loading is not configured. Call AddDynamicPluginLoading() during startup.");

        _loader.UnloadPlugin(pluginName);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ScanPluginDirectory()
    {
        if (_loader is null)
            throw new InvalidOperationException(
                "Dynamic plugin loading is not configured. Call AddDynamicPluginLoading() during startup.");

        return _loader.ScanAndLoadAll();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, PluginDescriptor> GetLoadedExternalPlugins()
    {
        if (_loader is null)
            return new Dictionary<string, PluginDescriptor>();

        return _loader.LoadedPlugins;
    }

    // ── Bootstrap ──

    /// <summary>
    /// Auto-installs and enables all built-in plugins at startup.
    /// For each registered feature (in topological order):
    /// <list type="bullet">
    ///   <item>If no state record → install then enable</item>
    ///   <item>If status is Installed → enable</item>
    ///   <item>If status is Disabled → skip (user explicitly disabled, respect that)</item>
    ///   <item>If status is Enabled → skip (already active)</item>
    /// </list>
    /// Each plugin is wrapped in its own try/catch so one failure does not block others.
    /// Module installation failures (e.g., Registry API unavailable) are logged as warnings.
    /// </summary>
    public async Task BootstrapBuiltInPluginsAsync(
        IServiceProvider services,
        CancellationToken ct = default)
        => await BootstrapBuiltInPluginsAsync(services, new PluginBootstrapOptions(), ct);

    /// <summary>
    /// Bootstrap built-in plugins with explicit options controlling which plugins to exclude.
    /// Excluded plugins and their transitive dependents are skipped during automatic
    /// install/enable but remain registered in the <see cref="PlatformFeatureRegistry"/>.
    /// </summary>
    public async Task BootstrapBuiltInPluginsAsync(
        IServiceProvider services,
        PluginBootstrapOptions options,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Bootstrapping built-in plugins ({Count} registered features)",
            _registry.AllFeatures.Count);

        // Build the effective exclusion set by cascading through dependencies.
        // If plugin A is excluded and B depends on A, B is automatically excluded too.
        var excludedPlugins = BuildExclusionSet(options.Exclude);

        if (excludedPlugins.Count > 0)
        {
            _logger.LogInformation(
                "Plugin bootstrap exclusions (from config): {ExcludedPlugins}",
                string.Join(", ", excludedPlugins.Order()));
        }

        var installed = 0;
        var enabled = 0;
        var skipped = 0;
        var excluded = 0;
        var failedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in _registry.AllFeatures)
        {
            // Skip plugins excluded by configuration
            if (excludedPlugins.Contains(feature.Name))
            {
                _logger.LogInformation(
                    "Plugin '{PluginName}': excluded by configuration — skipping bootstrap",
                    feature.Name);
                excluded++;
                continue;
            }

            // Skip plugins whose dependencies failed — they cannot be enabled
            var blockedDeps = feature.DependsOn.Where(d => failedPlugins.Contains(d)).ToList();
            if (blockedDeps.Count > 0)
            {
                _logger.LogError(
                    "Plugin '{PluginName}': skipped — blocked by failed dependencies: {Dependencies}",
                    feature.Name, string.Join(", ", blockedDeps));
                failedPlugins.Add(feature.Name);
                continue;
            }

            try
            {
                var state = await GetPluginStateAsync(feature.Name, ct);

                if (state is null)
                {
                    // First boot: install then enable
                    _logger.LogInformation("Plugin '{PluginName}': not installed — installing and enabling",
                        feature.Name);

                    try
                    {
                        await InstallPluginAsync(feature.Name, services, ct);
                        installed++;
                    }
                    catch (Exception installEx)
                    {
                        _logger.LogError(installEx,
                            "Plugin '{PluginName}': installation failed — will retry on next startup",
                            feature.Name);
                        failedPlugins.Add(feature.Name);
                        continue; // Cannot enable if install failed
                    }

                    try
                    {
                        await EnablePluginAsync(feature.Name, services, ct);
                        enabled++;
                    }
                    catch (Exception enableEx)
                    {
                        _logger.LogError(enableEx,
                            "Plugin '{PluginName}': installed but enable failed — will retry on next startup",
                            feature.Name);
                        failedPlugins.Add(feature.Name);
                    }
                }
                else if (state.Status == PluginStatus.Installed)
                {
                    // Previously installed but not yet enabled
                    _logger.LogInformation("Plugin '{PluginName}': installed but not enabled — enabling",
                        feature.Name);

                    try
                    {
                        await EnablePluginAsync(feature.Name, services, ct);
                        enabled++;
                    }
                    catch (Exception enableEx)
                    {
                        _logger.LogError(enableEx,
                            "Plugin '{PluginName}': enable failed — will retry on next startup",
                            feature.Name);
                        failedPlugins.Add(feature.Name);
                    }
                }
                else if (state.Status == PluginStatus.Disabled)
                {
                    _logger.LogDebug("Plugin '{PluginName}': explicitly disabled — skipping",
                        feature.Name);
                    skipped++;
                }
                else if (state.Status == PluginStatus.Enabled)
                {
                    _logger.LogDebug("Plugin '{PluginName}': already enabled — skipping",
                        feature.Name);
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Plugin '{PluginName}' failed during bootstrap — skipping",
                    feature.Name);
                failedPlugins.Add(feature.Name);
            }
        }

        // Post-loop sweep: re-activate global features for all enabled plugins.
        // In-memory global feature state is lost between restarts, so we must ensure
        // every enabled IGlobalFeature is activated regardless of which bootstrap path
        // was taken (fresh install, already enabled, or newly enabled).
        foreach (var feature in _registry.AllFeatures)
        {
            if (failedPlugins.Contains(feature.Name) || excludedPlugins.Contains(feature.Name))
                continue;

            if (feature is not IGlobalFeature)
                continue;

            if (_stateCache.TryGetValue(feature.Name, out var cachedStatus) && cachedStatus == PluginStatus.Enabled)
            {
                ActivateGlobalFeatureIfSupported(feature.Name);
            }
        }

        // Apply RLS policies if MultiTenancy is enabled (idempotent — safe on every startup)
        if (!failedPlugins.Contains("TenantIsolation") && !excludedPlugins.Contains("TenantIsolation"))
        {
            var tenantState = _stateCache.GetValueOrDefault("TenantIsolation");
            if (tenantState == PluginStatus.Enabled)
            {
                _logger.LogInformation("Applying RLS policies on startup (MultiTenancy enabled)");
                await ApplyRlsPoliciesAsync(services, ct);
            }
        }

        _logger.LogInformation(
            "Plugin bootstrap complete: {Installed} installed, {Enabled} enabled, {Skipped} skipped, {Excluded} excluded, {Failed} failed",
            installed, enabled, skipped, excluded, failedPlugins.Count);
    }

    // ── RLS Policy Management ──

    private const string TenantRlsPolicyName = "tenant_isolation_policy";
    private const string TenantRlsConfigKey = "app.current_tenant_id";

    /// <summary>
    /// Apply PostgreSQL Row-Level Security policies to all tenant-scoped entity tables.
    /// Called when MultiTenancy plugin is enabled or on startup (defense-in-depth).
    /// Idempotent: uses DROP POLICY IF EXISTS before CREATE.
    /// </summary>
    private async Task ApplyRlsPoliciesAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var cacheManager = services.GetService<MetaModelCacheManager>();
        if (cacheManager == null)
        {
            _logger.LogWarning("Cannot apply RLS policies: MetaModelCacheManager not available");
            return;
        }

        var cache = await cacheManager.GetCacheAsync(ct);
        var tenantEntities = cache.Model.Entities
            .Where(e => e.TenantScoped && !string.IsNullOrEmpty(e.Namespace))
            .ToList();

        if (tenantEntities.Count == 0)
        {
            _logger.LogDebug("No tenant-scoped entities — skipping RLS policy application");
            return;
        }

        _logger.LogInformation("Applying RLS policies to {Count} tenant-scoped entities", tenantEntities.Count);

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        var applied = 0;
        var skipped = 0;

        foreach (var entity in tenantEntities)
        {
            var schema = NamingConvention.GetSchemaName(entity.Namespace);
            var table = NamingConvention.ToSnakeCase(entity.Name);
            var qualifiedTable = $"\"{schema}\".\"{table}\"";

            try
            {
                // Check if table exists (may not if module hasn't been compiled with initSchema)
                await using var checkCmd = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table)",
                    connection);
                checkCmd.Parameters.AddWithValue("@schema", schema);
                checkCmd.Parameters.AddWithValue("@table", table);
                var exists = (bool)(await checkCmd.ExecuteScalarAsync(ct))!;

                if (!exists)
                {
                    skipped++;
                    continue;
                }

                // Check if table has tenant_id column
                await using var colCmd = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table AND column_name = 'tenant_id')",
                    connection);
                colCmd.Parameters.AddWithValue("@schema", schema);
                colCmd.Parameters.AddWithValue("@table", table);
                var hasTenantCol = (bool)(await colCmd.ExecuteScalarAsync(ct))!;

                if (!hasTenantCol)
                {
                    skipped++;
                    continue;
                }

                var rlsSql =
                    $"ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;\n" +
                    $"DROP POLICY IF EXISTS {TenantRlsPolicyName} ON {qualifiedTable};\n" +
                    $"CREATE POLICY {TenantRlsPolicyName} ON {qualifiedTable} FOR ALL " +
                    $"USING (tenant_id = current_setting('{TenantRlsConfigKey}', true)::UUID);";

                await using var rlsCmd = new NpgsqlCommand(rlsSql, connection);
                await rlsCmd.ExecuteNonQueryAsync(ct);
                applied++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply RLS policy to {Table} — skipping", qualifiedTable);
                skipped++;
            }
        }

        _logger.LogInformation("RLS policies applied: {Applied} tables, {Skipped} skipped", applied, skipped);
    }

    /// <summary>
    /// Drop PostgreSQL Row-Level Security policies from all tenant-scoped entity tables.
    /// Called when MultiTenancy plugin is disabled.
    /// </summary>
    private async Task DropRlsPoliciesAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var cacheManager = services.GetService<MetaModelCacheManager>();
        if (cacheManager == null) return;

        var cache = await cacheManager.GetCacheAsync(ct);
        var tenantEntities = cache.Model.Entities
            .Where(e => e.TenantScoped && !string.IsNullOrEmpty(e.Namespace))
            .ToList();

        if (tenantEntities.Count == 0) return;

        _logger.LogInformation("Dropping RLS policies from {Count} entities", tenantEntities.Count);

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);

        foreach (var entity in tenantEntities)
        {
            var schema = NamingConvention.GetSchemaName(entity.Namespace);
            var table = NamingConvention.ToSnakeCase(entity.Name);
            var qualifiedTable = $"\"{schema}\".\"{table}\"";

            try
            {
                var sql =
                    $"DROP POLICY IF EXISTS {TenantRlsPolicyName} ON {qualifiedTable};\n" +
                    $"ALTER TABLE {qualifiedTable} DISABLE ROW LEVEL SECURITY;";

                await using var cmd = new NpgsqlCommand(sql, connection);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to drop RLS policy from {Table}", qualifiedTable);
            }
        }
    }

    // ── Private helpers ──

    /// <summary>
    /// If the feature implements IGlobalFeature, activates it in the registry.
    /// Global features apply to ALL entities by default — no settings toggle required.
    /// Entities must use @SystemScoped annotation to opt out.
    /// </summary>
    private void ActivateGlobalFeatureIfSupported(string featureName)
    {
        var feature = _registry.GetFeature(featureName);
        if (feature is not IGlobalFeature)
            return;

        _registry.ActivateGlobalFeature(featureName);
        _logger.LogInformation(
            "Feature '{FeatureName}' activated in global mode — applies to ALL entities (use @SystemScoped to opt out)",
            featureName);
    }

    /// <summary>
    /// Build the full exclusion set by expanding the user-provided list with
    /// transitive dependents. If plugin A is excluded and B depends on A,
    /// B must also be excluded (otherwise it would fail the dependency check anyway).
    /// </summary>
    private HashSet<string> BuildExclusionSet(IEnumerable<string> configuredExclusions)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in configuredExclusions)
        {
            if (_registry.GetFeature(name) == null)
            {
                _logger.LogWarning(
                    "Plugin exclusion '{PluginName}' in config does not match any registered feature — ignored",
                    name);
                continue;
            }
            excluded.Add(name);
        }

        if (excluded.Count == 0)
            return excluded;

        // Cascade: find all features that transitively depend on an excluded feature.
        // Iterate in topological order (AllFeatures is already sorted), so we catch
        // cascading dependencies in a single pass.
        foreach (var feature in _registry.AllFeatures)
        {
            if (excluded.Contains(feature.Name))
                continue;

            var blockedDeps = feature.DependsOn
                .Where(d => excluded.Contains(d))
                .ToList();

            if (blockedDeps.Count > 0)
            {
                _logger.LogWarning(
                    "Plugin '{PluginName}' auto-excluded: depends on excluded plugin(s): {Dependencies}",
                    feature.Name, string.Join(", ", blockedDeps));
                excluded.Add(feature.Name);
            }
        }

        return excluded;
    }

    private static PluginStateRecord ReadPluginState(NpgsqlDataReader reader)
    {
        var name = reader.GetString(reader.GetOrdinal("name"));
        var version = reader.GetInt32(reader.GetOrdinal("version"));
        var statusStr = reader.GetString(reader.GetOrdinal("status"));
        var installedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("installed_at"));

        var enabledAtOrdinal = reader.GetOrdinal("enabled_at");
        var enabledAt = reader.IsDBNull(enabledAtOrdinal)
            ? (DateTimeOffset?)null
            : reader.GetFieldValue<DateTimeOffset>(enabledAtOrdinal);

        var settingsOrdinal = reader.GetOrdinal("settings_json");
        var settingsJson = reader.IsDBNull(settingsOrdinal) ? "{}" : reader.GetString(settingsOrdinal);
        Dictionary<string, object?> settings;
        try
        {
            settings = JsonSerializer.Deserialize<Dictionary<string, object?>>(settingsJson, JsonOptions)
                ?? new Dictionary<string, object?>();
        }
        catch (System.Text.Json.JsonException)
        {
            settings = new Dictionary<string, object?>();
        }

        var status = statusStr switch
        {
            "installed" => PluginStatus.Installed,
            "enabled" => PluginStatus.Enabled,
            "disabled" => PluginStatus.Disabled,
            _ => throw new InvalidOperationException($"Unknown plugin status: '{statusStr}'")
        };

        return new PluginStateRecord(name, version, status, installedAt, enabledAt, settings);
    }

    private async Task<HashSet<int>> GetAppliedMigrationsAsync(string pluginName, CancellationToken ct)
    {
        const string sql = """
            SELECT version FROM core.plugin_migration_history
            WHERE plugin_name = @name
            ORDER BY version
            """;

        await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.Add(new NpgsqlParameter("@name", pluginName));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var versions = new HashSet<int>();
        while (await reader.ReadAsync(ct))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    /// <summary>
    /// Validates that no enabled plugin depends on the given plugin.
    ///
    /// NOTE: Race condition window — another thread could enable a dependent plugin between
    /// reading all states and checking dependents. A full fix would require database-level
    /// locking (e.g., SELECT ... FOR UPDATE or advisory locks), which is over-engineering
    /// for the current use case where plugin lifecycle operations are infrequent and typically
    /// performed by a single admin. The worst case is a misleading error message if the
    /// race is hit; no data corruption can occur since enable checks dependencies too.
    /// </summary>
    private async Task ValidateNoDependentsEnabledAsync(string name, CancellationToken ct)
    {
        var allStates = await GetAllPluginStatesAsync(ct);
        var enabledNames = allStates
            .Where(s => s.Status == PluginStatus.Enabled)
            .Select(s => s.Name)
            .ToHashSet();

        foreach (var featureName in _registry.AllFeatureNames)
        {
            if (!enabledNames.Contains(featureName))
                continue;

            var feature = FindFeature<IPlatformFeature>(featureName);
            if (feature is not null && feature.DependsOn.Contains(name))
            {
                throw new InvalidOperationException(
                    $"Cannot disable plugin '{name}': plugin '{featureName}' depends on it and is currently enabled");
            }
        }
    }

    private T? FindFeature<T>(string name) where T : class
    {
        return _registry.GetFeature(name) as T;
    }

    private static void ValidatePluginName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Regex.IsMatch(name, @"^[a-zA-Z0-9._-]+$"))
            throw new ArgumentException(
                $"Invalid plugin name: '{name}'. Only alphanumeric, dots, dashes, and underscores allowed.",
                nameof(name));
    }

    private static string ComputeChecksum(string sql)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sql));
        return Convert.ToHexStringLower(bytes);
    }
}
