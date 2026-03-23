# Plugin System Fixes Plan

Post-code-review fix plan for the plugin system. Each fix has exact file paths, line numbers, and code changes. Fixes are grouped into four parallel-safe work streams that touch non-overlapping files.

**Scope**: 2 critical, 3 high, 7 medium, 4 low fixes across 9 files.

---

## Work Streams Overview

| Stream | Files Touched | Fixes |
|--------|---------------|-------|
| **Stream 1** | `PluginManager.cs` | C1, C2, H3, M3, M4 |
| **Stream 2** | `PlatformFeatureRegistry.cs` | M5, M6, M7 |
| **Stream 3** | 6 infrastructure plugin files | H1, M1, M2, L1, L2, L3 |
| **Stream 4** | `Program.cs` | L4 |

---

## Stream 1: PluginManager Critical Fixes

**File**: `src/BMMDL.Runtime/Plugins/PluginManager.cs`

### C1. OnInstalledAsync fail leaves plugin permanently stuck

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem** (lines 167-204): The state row INSERT (`status='installed'`) happens at line 168-177 BEFORE `OnInstalledAsync` is called at line 204. If `OnInstalledAsync` throws, the plugin row persists with status `installed`. On next startup, `BootstrapBuiltInPluginsAsync` sees `status == Installed` (line 846) and jumps to `EnablePluginAsync`, never retrying `OnInstalledAsync`.

**Fix strategy**: Wrap INSERT + OnInstalledAsync in a single database transaction. If OnInstalledAsync fails, the INSERT is rolled back, so the plugin has no state row and bootstrap retries from scratch on next startup.

**Current code** (lines 167-205):
```csharp
// Insert state row
const string sql = """
    INSERT INTO core.plugin_state (name, version, status, installed_at, settings_json)
    VALUES (@name, 1, 'installed', now(), '{}')
    RETURNING name, version, status, installed_at, enabled_at, settings_json
    """;

await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
await using var cmd = new NpgsqlCommand(sql, connection);
cmd.Parameters.Add(new NpgsqlParameter("@name", name));

PluginStateRecord state;
try
{
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    // ...
}
catch (NpgsqlException ex) when (ex.SqlState == "23505") { ... }

_stateCache[name] = state.Status;

// Call lifecycle hook
var lifecycle = FindFeature<IPluginLifecycle>(name);
if (lifecycle is not null)
{
    var ctx = new PluginContext { Services = services, Settings = state.Settings };
    await lifecycle.OnInstalledAsync(ctx, ct);
}
```

**New code**:
```csharp
// Open connection + transaction to atomically insert state + run lifecycle hook.
// If OnInstalledAsync fails, the INSERT rolls back so bootstrap retries on next startup.
await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);
await using var txn = await connection.BeginTransactionAsync(ct);

const string sql = """
    INSERT INTO core.plugin_state (name, version, status, installed_at, settings_json)
    VALUES (@name, 1, 'installed', now(), '{}')
    RETURNING name, version, status, installed_at, enabled_at, settings_json
    """;

await using var cmd = new NpgsqlCommand(sql, connection, txn);
cmd.Parameters.Add(new NpgsqlParameter("@name", name));

PluginStateRecord state;
try
{
    await using var reader = await cmd.ExecuteReaderAsync(ct);

    if (!await reader.ReadAsync(ct))
        throw new InvalidOperationException($"Failed to insert plugin state for '{name}'");

    state = ReadPluginState(reader);
}
catch (NpgsqlException ex) when (ex.SqlState == "23505")
{
    throw new InvalidOperationException($"Plugin '{name}' is already installed (concurrent install detected)", ex);
}

_stateCache[name] = state.Status;

// Call lifecycle hook INSIDE the transaction.
// If this throws, the INSERT is rolled back.
var lifecycle = FindFeature<IPluginLifecycle>(name);
if (lifecycle is not null)
{
    var ctx = new PluginContext
    {
        Services = services,
        Settings = state.Settings
    };
    await lifecycle.OnInstalledAsync(ctx, ct);
}

await txn.CommitAsync(ct);
```

**Tests**:
- `InstallPluginAsync_OnInstalledAsyncThrows_RollsBackStateRow`: Verify no state row exists after lifecycle hook throws.
- `BootstrapBuiltInPluginsAsync_AfterFailedInstall_RetriesInstallOnNextCall`: Verify full install+enable flow runs again.

---

### C2. Global features not re-activated after restart

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem** (lines 871-876): In `BootstrapBuiltInPluginsAsync`, when a plugin's state is already `Enabled`, the code logs "already enabled -- skipping" and does nothing else. But `_globalFeatures` (in `PlatformFeatureRegistry`) is a `ConcurrentDictionary` that starts empty on each process restart. `ActivateGlobalFeatureIfSupported` is only called inside `EnablePluginAsync` (line 283), which is never reached for already-enabled plugins.

**Result**: After restart, any `IGlobalFeature` plugin (e.g., AuditField, SoftDelete) silently loses global mode.

**Current code** (lines 871-876):
```csharp
else if (state.Status == PluginStatus.Enabled)
{
    _logger.LogDebug("Plugin '{PluginName}': already enabled — skipping",
        feature.Name);
    skipped++;
}
```

**New code** (lines 871-878):
```csharp
else if (state.Status == PluginStatus.Enabled)
{
    _logger.LogDebug("Plugin '{PluginName}': already enabled — re-activating global feature if supported",
        feature.Name);
    // Re-activate global mode: _globalFeatures ConcurrentDictionary starts empty on each restart
    ActivateGlobalFeatureIfSupported(feature.Name);
    skipped++;
}
```

**Tests**:
- `BootstrapBuiltInPluginsAsync_AlreadyEnabledGlobalFeature_ReactivatesGlobalMode`: Mock an `IGlobalFeature` plugin with status `Enabled` in DB. After bootstrap, verify `registry.IsFeatureGloballyActive(name)` returns true.

---

### H3. Migration execution has no transaction

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem** (lines 510-536): `RunPendingMigrationsAsync` executes each migration's `UpSql` and then records it in `plugin_migration_history`, but these two operations are not wrapped in a transaction. If the process crashes between executing `UpSql` and recording the history row, the migration DDL is applied but not tracked, causing a duplicate-apply error on next startup.

**Current code** (lines 510-536):
```csharp
await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);

foreach (var migration in pending)
{
    // Execute the UpSql
    await using var migCmd = new NpgsqlCommand(migration.UpSql, connection);
    await migCmd.ExecuteNonQueryAsync(ct);

    // Record in migration history
    var checksum = ComputeChecksum(migration.UpSql);
    const string insertSql = """...""";

    await using var histCmd = new NpgsqlCommand(insertSql, connection);
    // ...params...
    await histCmd.ExecuteNonQueryAsync(ct);
}
```

**New code**:
```csharp
await using var connection = await _connectionFactory.GetConnectionAsync(ct: ct);

foreach (var migration in pending)
{
    _logger.LogDebug(
        "Applying migration v{Version} for plugin '{PluginName}': {Description}",
        migration.Version, pluginName, migration.Description);

    // Wrap each migration in a transaction to ensure UpSql + history record are atomic.
    await using var txn = await connection.BeginTransactionAsync(ct);

    // Execute the UpSql
    await using var migCmd = new NpgsqlCommand(migration.UpSql, connection, txn);
    await migCmd.ExecuteNonQueryAsync(ct);

    // Record in migration history
    var checksum = ComputeChecksum(migration.UpSql);

    const string insertSql = """
        INSERT INTO core.plugin_migration_history (plugin_name, version, description, applied_at, checksum)
        VALUES (@name, @version, @description, now(), @checksum)
        """;

    await using var histCmd = new NpgsqlCommand(insertSql, connection, txn);
    histCmd.Parameters.Add(new NpgsqlParameter("@name", pluginName));
    histCmd.Parameters.Add(new NpgsqlParameter("@version", migration.Version));
    histCmd.Parameters.Add(new NpgsqlParameter("@description", migration.Description));
    histCmd.Parameters.Add(new NpgsqlParameter("@checksum", checksum));
    await histCmd.ExecuteNonQueryAsync(ct);

    await txn.CommitAsync(ct);
}
```

**Tests**:
- `RunPendingMigrationsAsync_UpSqlSucceeds_HistoryInsertFails_RollsBack`: Force history INSERT failure, verify UpSql effect is rolled back.

---

### M3. UninstallPluginAsync calls OnUninstalledAsync AFTER down-migrations

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem** (lines 372-416): The `IPluginLifecycle` doc contract (in `IPluginLifecycle.cs:26-28`) says `OnUninstalledAsync` is "Called before the plugin is uninstalled and down-migrations run." But the current code runs down-migrations first (lines 372-403), then calls `OnUninstalledAsync` (lines 406-416).

This matters because `OnUninstalledAsync` may need to read data from the plugin's tables before they are dropped by down-migrations.

**Fix**: Move `OnUninstalledAsync` call BEFORE the down-migration loop.

**Current code** (lines 372-416):
```csharp
// Run down-migrations in reverse order
var migrationProvider = FindFeature<IMigrationProvider>(name);
if (migrationProvider is not null)
{
    // ... runs all down-migrations ...
}

// Call lifecycle hook before deleting state
var lifecycle = FindFeature<IPluginLifecycle>(name);
if (lifecycle is not null)
{
    var ctx = new PluginContext { ... };
    await lifecycle.OnUninstalledAsync(ctx, ct);
}
```

**New code**:
```csharp
// Call lifecycle hook BEFORE running down-migrations.
// The plugin may need to read its tables before they are dropped.
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

// Run down-migrations in reverse order
var migrationProvider = FindFeature<IMigrationProvider>(name);
if (migrationProvider is not null)
{
    // ... runs all down-migrations (unchanged) ...
}
```

**Tests**:
- `UninstallPluginAsync_CallsOnUninstalledAsync_BeforeDownMigrations`: Use an `IMigrationProvider` + `IPluginLifecycle` mock. Verify `OnUninstalledAsync` is invoked before any down-migration SQL executes.

---

### M4. Settings notification fires before DB persist

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem** (lines 444-472): `UpdateSettingsAsync` calls `OnSettingsChangedAsync` (line 448) BEFORE persisting the new settings to the database (lines 460-464). If the DB write fails, the plugin has already been notified of settings that were never actually saved.

**Current code** (lines 444-472):
```csharp
// Validate via ISettingsProvider if present
var settingsProvider = FindFeature<ISettingsProvider>(name);
if (settingsProvider is not null)
{
    await settingsProvider.OnSettingsChangedAsync(settings, ct);  // <-- fires BEFORE persist
}

var settingsJson = JsonSerializer.Serialize(settings, JsonOptions);
// ... DB UPDATE ...
```

**New code**:
```csharp
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

// Notify AFTER successful persist
var settingsProvider = FindFeature<ISettingsProvider>(name);
if (settingsProvider is not null)
{
    await settingsProvider.OnSettingsChangedAsync(settings, ct);
}

_logger.LogInformation("Settings updated for plugin '{PluginName}'", name);
return state;
```

**Tests**:
- `UpdateSettingsAsync_DbWriteFails_DoesNotCallOnSettingsChanged`: Mock DB failure, verify `OnSettingsChangedAsync` is never called.

---

## Stream 2: PlatformFeatureRegistry Fixes

**File**: `src/BMMDL.Runtime/Plugins/PlatformFeatureRegistry.cs`

### M5. Menu/pages returned for disabled plugins

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem** (lines 165-177): `GetAggregatedMenuItems()` and `GetAggregatedPages()` iterate ALL registered `MenuContributors` and `PageProviders` regardless of whether the plugin is currently enabled. Disabled plugins still contribute menu items and admin pages.

**Fix**: Accept a predicate or set of enabled plugin names. Since the registry doesn't track enabled state (that's `PluginManager`'s job), the cleanest approach is to accept a `Func<string, bool>` or `IReadOnlySet<string>` parameter.

**Current code** (lines 165-177):
```csharp
public IReadOnlyList<PluginMenuItem> GetAggregatedMenuItems()
    => MenuContributors
        .SelectMany(c => c.GetMenuItems())
        .OrderBy(m => m.Order)
        .ToList();

public IReadOnlyList<PluginPageDefinition> GetAggregatedPages()
    => PageProviders
        .SelectMany(p => p.GetPages())
        .ToList();
```

**New code**:
```csharp
/// <summary>
/// Get all menu items from enabled plugins, sorted by order.
/// Falls back to all plugins if no filter is provided (backward compat).
/// </summary>
public IReadOnlyList<PluginMenuItem> GetAggregatedMenuItems(Func<string, bool>? isEnabled = null)
    => MenuContributors
        .Where(c => isEnabled is null || isEnabled(c.Name))
        .SelectMany(c => c.GetMenuItems())
        .OrderBy(m => m.Order)
        .ToList();

/// <summary>
/// Get all page definitions from enabled plugins.
/// Falls back to all plugins if no filter is provided (backward compat).
/// </summary>
public IReadOnlyList<PluginPageDefinition> GetAggregatedPages(Func<string, bool>? isEnabled = null)
    => PageProviders
        .Where(p => isEnabled is null || isEnabled(p.Name))
        .SelectMany(p => p.GetPages())
        .ToList();
```

**Callers to update**: Search for `GetAggregatedMenuItems()` and `GetAggregatedPages()` call sites in Runtime.Api and pass `pluginManager.IsPluginEnabled` as the predicate:
```csharp
registry.GetAggregatedMenuItems(pluginManager.IsPluginEnabled)
registry.GetAggregatedPages(pluginManager.IsPluginEnabled)
```

**Tests**:
- `GetAggregatedMenuItems_WithEnabledFilter_ExcludesDisabledPlugins`
- `GetAggregatedPages_WithEnabledFilter_ExcludesDisabledPlugins`
- `GetAggregatedMenuItems_NullFilter_ReturnsAll` (backward compat)

---

### M6. Case-sensitive ByName dictionary

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem** (line 316): The `ByName` dictionary in `RegistrySnapshot` uses the default `StringComparer.Ordinal`. Plugin names are generally PascalCase, but lookups from external sources (API, config) could use different casing. The `_stateCache` in `PluginManager` already uses `StringComparer.OrdinalIgnoreCase` (line 29), creating an inconsistency.

**Current code** (line 316):
```csharp
ByName = sorted.ToDictionary(f => f.Name);
```

**New code** (line 316):
```csharp
ByName = sorted.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
```

Also update `TopologicalSort` (line 220):
```csharp
// Current:
var byName = new Dictionary<string, IPlatformFeature>(features.Count);
// New:
var byName = new Dictionary<string, IPlatformFeature>(features.Count, StringComparer.OrdinalIgnoreCase);
```

**Tests**:
- `GetFeature_CaseInsensitiveName_ReturnsFeature`: Register feature "AuditField", lookup with "auditfield".

---

### M7. Unstable PriorityQueue sort for same-stage features

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem** (lines 252-270): `PriorityQueue<string, int>` uses `Stage` as priority. When multiple features share the same `Stage`, .NET's `PriorityQueue` does not guarantee FIFO ordering among equal-priority elements. This means the topological sort output can vary between runs, making startup order non-deterministic.

**Fix**: Use a composite priority key that includes both Stage and alphabetical name to break ties deterministically.

**Current code** (lines 252-269):
```csharp
var queue = new PriorityQueue<string, int>();
foreach (var (name, deg) in inDegree)
{
    if (deg == 0)
        queue.Enqueue(name, byName[name].Stage);
}
// ...
queue.Enqueue(dependent, byName[dependent].Stage);
```

**New code**: Use `PriorityQueue<string, (int Stage, string Name)>` with a custom comparer:
```csharp
var queue = new PriorityQueue<string, (int Stage, string Name)>(
    Comparer<(int Stage, string Name)>.Create((a, b) =>
    {
        var cmp = a.Stage.CompareTo(b.Stage);
        return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
    }));

foreach (var (name, deg) in inDegree)
{
    if (deg == 0)
        queue.Enqueue(name, (byName[name].Stage, name));
}

// ... inside the while loop:
foreach (var dependent in dependents[name])
{
    inDegree[dependent]--;
    if (inDegree[dependent] == 0)
        queue.Enqueue(dependent, (byName[dependent].Stage, dependent));
}
```

**Tests**:
- `TopologicalSort_SameStageFeatures_SortedAlphabetically`: Register features A (stage 10) and B (stage 10) with no deps. Verify A comes before B.

---

## Stream 3: Infrastructure Plugin Cleanup

**Files**:
- `src/BMMDL.Runtime/Plugins/Features/AuditLoggingPlugin.cs`
- `src/BMMDL.Runtime/Plugins/Features/EventOutboxPlugin.cs`
- `src/BMMDL.Runtime/Plugins/Features/ReportingPlugin.cs`
- `src/BMMDL.Runtime/Plugins/Features/WebhookPlugin.cs`
- `src/BMMDL.Runtime/Plugins/Features/CollaborationPlugin.cs`
- `src/BMMDL.Runtime/Plugins/Features/UserPreferencesPlugin.cs`

### H1. Connection handling inconsistency

- [ ] **Fix applied**
- [ ] **Tests written**

**Problem**: Three plugins (`AuditLoggingPlugin`, `EventOutboxPlugin`, `ReportingPlugin`) use `connectionFactory.ConnectionString` + `new NpgsqlConnection(connectionString)` to get a database connection, while the other three (`WebhookPlugin`, `CollaborationPlugin`, `UserPreferencesPlugin`) use the preferred `connectionFactory.GetConnectionAsync()` pattern. The manual pattern bypasses any connection pooling, middleware, or instrumentation that `GetConnectionAsync` provides.

**Fixes** (one per affected plugin):

#### AuditLoggingPlugin.cs (lines 72, 95-96)

Current:
```csharp
var connectionString = connectionFactory.ConnectionString;
// ... (sql string) ...
await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync(ct);
```

New:
```csharp
await using var conn = await connectionFactory.GetConnectionAsync(ct: ct);
```

Remove line 72 entirely (`var connectionString = connectionFactory.ConnectionString;`).
Replace lines 95-96 with the single line above.

#### EventOutboxPlugin.cs (lines 72, 96-97)

Same pattern. Current:
```csharp
var connectionString = connectionFactory.ConnectionString;
// ...
await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync(ct);
```

New:
```csharp
await using var conn = await connectionFactory.GetConnectionAsync(ct: ct);
```

Remove line 72. Replace lines 96-97.

#### ReportingPlugin.cs (lines 72, 98-99)

Same pattern. Current:
```csharp
var connectionString = connectionFactory.ConnectionString;
// ...
await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync(ct);
```

New:
```csharp
await using var conn = await connectionFactory.GetConnectionAsync(ct: ct);
```

Remove line 72. Replace lines 98-99.

---

### M1. Hardcoded "platform" schema string

- [ ] **Fix applied**

**Problem**: Three plugins use hardcoded `"platform"` string instead of `SchemaConstants.PlatformSchema`.

#### AuditLoggingPlugin.cs (line 74)

Current:
```csharp
CREATE SCHEMA IF NOT EXISTS platform;
```

New:
```csharp
CREATE SCHEMA IF NOT EXISTS {SchemaConstants.PlatformSchema};
```

#### EventOutboxPlugin.cs (line 74)

Same fix as above.

#### UserPreferencesPlugin.cs (line 87)

Current (raw string literal with `$$` and `{{`):
```csharp
string sql = $$"""
    CREATE SCHEMA IF NOT EXISTS platform;
    CREATE TABLE IF NOT EXISTS {{SchemaConstants.UserPreferencesTable}} (
```

New:
```csharp
string sql = $$"""
    CREATE SCHEMA IF NOT EXISTS {{SchemaConstants.PlatformSchema}};
    CREATE TABLE IF NOT EXISTS {{SchemaConstants.UserPreferencesTable}} (
```

---

### M2. Log level inconsistency for DDL failure

- [ ] **Fix applied**

**Problem**: `CollaborationPlugin` (line 78) and `UserPreferencesPlugin` (line 112) use `LogError` for DDL failure in `OnInstalledAsync`, while all other plugins use `LogWarning`. DDL failure during install is expected when the DB is not yet available, so `LogWarning` is appropriate.

#### CollaborationPlugin.cs (line 78)

Current:
```csharp
_logger?.LogError(ex, "Collaboration plugin: Failed to create tables during install.");
```

New:
```csharp
_logger?.LogWarning(ex, "Collaboration plugin: Failed to create tables during install. Tables may need to be created manually.");
```

#### UserPreferencesPlugin.cs (line 112)

Current:
```csharp
_logger?.LogError(ex, "UserPreferences plugin: failed to create user_preferences table.");
```

New:
```csharp
_logger?.LogWarning(ex, "UserPreferences plugin: failed to create user_preferences table. The table may need to be created manually.");
```

---

### L1. Unnecessary null-conditional on ctx.Services

- [ ] **Fix applied**

**Problem**: `PluginContext.Services` is declared as `required IServiceProvider Services { get; init; }` (in `PluginTypes.cs:91`), so it can never be null. The `?.` operator is misleading and inconsistent.

**Affected files and lines** (all 6 infrastructure plugins):

| File | Line | Current | New |
|------|------|---------|-----|
| `AuditLoggingPlugin.cs` | 63 | `ctx.Services?.GetService<...>()` | `ctx.Services.GetService<...>()` |
| `EventOutboxPlugin.cs` | 63 | `ctx.Services?.GetService<...>()` | `ctx.Services.GetService<...>()` |
| `ReportingPlugin.cs` | 63 | `ctx.Services?.GetService<...>()` | `ctx.Services.GetService<...>()` |
| `WebhookPlugin.cs` | 63 | `ctx.Services?.GetService<...>()` | `ctx.Services.GetService<...>()` |
| `CollaborationPlugin.cs` | 62 | `ctx.Services?.GetService<...>()` | `ctx.Services.GetService<...>()` |
| `UserPreferencesPlugin.cs` | 77 | `ctx.Services?.GetService<...>()` | `ctx.Services.GetService<...>()` |

Also update the null checks from `== null` to `is null` where applicable (see L2).

---

### L2. Standardize null checks to `is null`

- [ ] **Fix applied**

Three plugins use `== null` while the other three use `is null`. Standardize to `is null` per C# best practices.

| File | Line | Current | New |
|------|------|---------|-----|
| `AuditLoggingPlugin.cs` | 64 | `if (connectionFactory == null)` | `if (connectionFactory is null)` |
| `EventOutboxPlugin.cs` | 64 | `if (connectionFactory == null)` | `if (connectionFactory is null)` |
| `ReportingPlugin.cs` | 64 | `if (connectionFactory == null)` | `if (connectionFactory is null)` |

The other three already use `is null` -- no changes needed.

---

### L3. Remove unused `using BMMDL.Runtime.Plugins.Contexts`

- [ ] **Fix applied**

Four infrastructure plugins import the `Contexts` namespace but use no types from it. `PluginContext` is in the parent namespace `BMMDL.Runtime.Plugins`, not `Contexts`.

| File | Line | Action |
|------|------|--------|
| `AuditLoggingPlugin.cs` | 4 | Remove `using BMMDL.Runtime.Plugins.Contexts;` |
| `EventOutboxPlugin.cs` | 4 | Remove `using BMMDL.Runtime.Plugins.Contexts;` |
| `ReportingPlugin.cs` | 4 | Remove `using BMMDL.Runtime.Plugins.Contexts;` |
| `CollaborationPlugin.cs` | 4 | Remove `using BMMDL.Runtime.Plugins.Contexts;` |

Note: `WebhookPlugin.cs` and `UserPreferencesPlugin.cs` do NOT have this import -- no changes needed.

---

## Stream 4: Program.cs Cleanup

**File**: `src/BMMDL.Runtime.Api/Program.cs`

### L4. Bootstrap cast has no else branch

- [ ] **Fix applied**

**Problem** (lines 559-565): The `if (pluginManager is PluginManager pm)` cast has no `else` branch. If the DI container returns a different `IPluginManager` implementation (e.g., in tests or a decoration scenario), bootstrap silently does nothing and no plugins are initialized.

**Current code** (lines 557-565):
```csharp
{
    using var scope = app.Services.CreateScope();
    var pluginManager = scope.ServiceProvider.GetRequiredService<IPluginManager>();
    if (pluginManager is PluginManager pm)
    {
        pm.EnsurePluginTablesAsync().GetAwaiter().GetResult();
        pm.BootstrapBuiltInPluginsAsync(scope.ServiceProvider).GetAwaiter().GetResult();
    }
}
```

**New code**:
```csharp
{
    using var scope = app.Services.CreateScope();
    var pluginManager = scope.ServiceProvider.GetRequiredService<IPluginManager>();
    if (pluginManager is PluginManager pm)
    {
        pm.EnsurePluginTablesAsync().GetAwaiter().GetResult();
        pm.BootstrapBuiltInPluginsAsync(scope.ServiceProvider).GetAwaiter().GetResult();
    }
    else
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(
            "Plugin bootstrap skipped: IPluginManager resolved to {Type} instead of PluginManager. " +
            "Built-in plugins will not be auto-installed.",
            pluginManager.GetType().FullName);
    }
}
```

---

## Deferred / Skipped Items

### H2. No distributed lock for bootstrap (DOCUMENT ONLY)

Multiple instances starting concurrently could race to install the same plugin. A proper fix requires `pg_advisory_lock` in `BootstrapBuiltInPluginsAsync`. Deferred because:
- Single-instance deployments are the norm for now.
- The `23505` unique_violation catch in `InstallPluginAsync` (line 188) provides a safety net.

### L5. Mutable Settings dictionary (SKIP)

`PluginContext.Settings` is `Dictionary<string, object?>` (mutable). Could be `IReadOnlyDictionary`. Skipped because `PluginContext` is internal and short-lived.

### L6. Sync-over-async in Program.cs (SKIP)

`GetAwaiter().GetResult()` is the standard ASP.NET Core startup pattern. No sync context in the startup path, so this is safe.

---

## Execution Checklist

All four streams can be executed in parallel since they touch non-overlapping files.

### Pre-flight
- [ ] `dotnet build BMMDL.sln` passes
- [ ] `dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj` passes

### Stream 1 (PluginManager.cs)
- [ ] C1: Transactional install
- [ ] C2: Re-activate global features on restart
- [ ] H3: Transactional migrations
- [ ] M3: Uninstall lifecycle order
- [ ] M4: Settings notification after persist
- [ ] Tests for all 5 fixes

### Stream 2 (PlatformFeatureRegistry.cs)
- [ ] M5: Enabled-state filtering for menu/pages
- [ ] M6: Case-insensitive ByName dictionary
- [ ] M7: Deterministic PriorityQueue tiebreaker
- [ ] Tests for all 3 fixes
- [ ] Update callers of GetAggregatedMenuItems/GetAggregatedPages

### Stream 3 (6 infrastructure plugins)
- [ ] H1: Fix connection handling in AuditLogging, EventOutbox, Reporting
- [ ] M1: Replace hardcoded "platform" in AuditLogging, EventOutbox, UserPreferences
- [ ] M2: Fix log levels in Collaboration, UserPreferences
- [ ] L1: Remove null-conditional on ctx.Services (all 6)
- [ ] L2: Standardize null checks (3 files)
- [ ] L3: Remove unused Contexts import (4 files)

### Stream 4 (Program.cs)
- [ ] L4: Add else branch with warning log

### Post-flight
- [ ] `dotnet build BMMDL.sln` passes
- [ ] `dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj` passes
- [ ] `dotnet clean && dotnet build BMMDL.sln` (ensure no stale DLLs)
