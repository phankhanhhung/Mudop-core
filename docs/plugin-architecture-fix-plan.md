# Plugin Architecture Fix Plan

## Overview

Fixes 10 architecture issues identified in the plugin system review, categorized by severity.

## Issue Summary

| ID | Severity | Issue | Files Affected |
|----|----------|-------|----------------|
| C1 | Critical | Exclusion config doesn't prevent service registration | PlatformFeatureExtensions.cs, Program.cs |
| C2 | Critical | External plugin IServiceContributor never called | PlatformFeatureExtensions.cs, PluginDirectoryLoader.cs |
| C3 | Critical | Infrastructure controllers lack RequiresPlugin guards | 6+ controllers |
| I1 | Important | IBrokerAdapter hardcoded in Program.cs (cross-layer) | Program.cs, new WebhookApiPlugin.cs |
| I2 | Important | Disable plugin doesn't deactivate services | Covered by C3 (RequiresPlugin) |
| I3 | Important | No table cleanup on uninstall | 6 infrastructure plugins |
| I4 | Important | Service lifetime inconsistency (UserPreferences) | UserPreferencesPlugin.cs |
| I5 | Important | Hidden dependency chain across assemblies | EventOutboxPlugin.cs (documentation) |
| D2 | Design | Bootstrap error isolation too aggressive | PluginManager.cs |
| D3 | Design | ConnectionString naming confusion | Program.cs (documentation) |

## Fix Details

### C1: Exclusion-Aware Service Registration

**Problem**: `AddPlatformFeatures()` calls `RegisterServices()` on ALL discovered features before bootstrap exclusion config is read. Excluded plugins have services in DI but no backing tables.

**Fix**:
1. Read `PluginBootstrapOptions` before `AddPlatformFeatures()`
2. Pass options to `AddPlatformFeatures()` as optional parameter
3. Build exclusion set (with transitive dependency cascade) inside the method
4. Skip `RegisterServices()` for excluded plugins

```csharp
// Program.cs — read config early
var bootstrapOptions = new PluginBootstrapOptions();
builder.Configuration.GetSection("Plugins:Bootstrap").Bind(bootstrapOptions);

builder.Services.AddPlatformFeatures(b => b
    .AddBuiltIn()
    .AddFromAssembly(typeof(IAdminApiProvider).Assembly),
    bootstrapOptions);  // NEW parameter
```

```csharp
// PlatformFeatureExtensions.cs — skip excluded
public static IServiceCollection AddPlatformFeatures(
    this IServiceCollection services,
    Action<PlatformFeatureBuilder> configure,
    PluginBootstrapOptions? bootstrapOptions = null)
{
    // ... existing code ...

    var excluded = BuildServiceExclusionSet(
        registry.AllFeatures, bootstrapOptions?.Exclude ?? []);

    foreach (var feature in builder.Features)
    {
        if (feature is IServiceContributor contributor
            && !excluded.Contains(feature.Name))
        {
            contributor.RegisterServices(services);
        }
    }
    return services;
}
```

### C2: External Plugin Service Registration

**Problem**: External plugins loaded via `PluginDirectoryLoader` after DI container is built. Their `IServiceContributor.RegisterServices()` is never called.

**Fix**: Add pre-scan phase in `AddDynamicPluginLoading()` that runs before `builder.Build()`:
1. Scan plugins directory for manifests
2. Load assemblies into default AssemblyLoadContext (not isolated)
3. Discover `IServiceContributor` implementations
4. Call `RegisterServices()` on each
5. Track pre-loaded plugins so `PluginDirectoryLoader` skips them later

**Trade-off**: Pre-loaded plugins cannot be unloaded at runtime (default AssemblyLoadContext is not collectible). Hot-loaded plugins (added after startup via API) still use isolated contexts but cannot register DI services.

**Two categories of external plugins**:
- **Boot-time**: In `plugins/` at startup → default context, full DI, no unloading
- **Runtime**: Added after startup → isolated context, no DI services, can unload

### C3: RequiresPlugin Guards on Controllers

**Problem**: Infrastructure API controllers work even when their plugin is disabled. Users see 404 in admin UI but API still responds.

**Fix**: Add `[RequiresPlugin("PluginName")]` attribute to all infrastructure controllers:
- `AuditLogController` → `[RequiresPlugin("AuditLogging")]`
- `CommentController` → `[RequiresPlugin("Collaboration")]`
- `ChangeRequestController` → `[RequiresPlugin("Collaboration")]`
- `ReportController` → `[RequiresPlugin("Reporting")]`
- `UserPreferenceController` → `[RequiresPlugin("UserPreferences")]`
- `WebhookController` → `[RequiresPlugin("Webhooks")]`
- `OutboxController` → `[RequiresPlugin("EventOutbox")]`
- `IntegrationController` → `[RequiresPlugin("Webhooks")]` (if exists)

This also resolves **I2** (disable doesn't deactivate services) — controllers return 404 when plugin disabled.

### I1: WebhookApiPlugin for IBrokerAdapter

**Problem**: `HttpBrokerAdapter` lives in `Runtime.Api` but is registered hardcoded in `Program.cs`. `WebhookPlugin` (in `Runtime`) can't reference it.

**Fix**: Create `WebhookApiPlugin` in `Runtime.Api/Plugins/` following the established companion plugin pattern (like `MultiTenancyApiPlugin`):

```csharp
// src/BMMDL.Runtime.Api/Plugins/WebhookApiPlugin.cs
public sealed class WebhookApiPlugin : IPlatformFeature, IServiceContributor
{
    public string Name => "Webhooks.Api";
    public IReadOnlyList<string> DependsOn => ["Webhooks"];
    public int Stage => 86;
    public bool AppliesTo(BmEntity entity) => false;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IBrokerAdapter>(sp =>
        {
            var webhookStore = sp.GetRequiredService<IWebhookStore>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<HttpBrokerAdapter>>();
            return new HttpBrokerAdapter(webhookStore, httpClientFactory, logger);
        });
    }
}
```

Remove hardcoded `IBrokerAdapter` registration from `Program.cs`.

### I3: Table Cleanup on Uninstall

**Problem**: All 6 infrastructure plugins log "tables should be dropped manually" on uninstall, leaving orphan tables.

**Fix**: Add `DROP TABLE IF EXISTS` in `OnUninstalledAsync()` for each plugin:

```csharp
public async Task OnUninstalledAsync(PluginContext ctx, CancellationToken ct)
{
    try
    {
        var connectionFactory = ctx.Services.GetService<ITenantConnectionFactory>();
        if (connectionFactory is null) return;

        await using var conn = await connectionFactory.GetConnectionAsync(ct: ct);
        await using var cmd = new NpgsqlCommand(
            $"DROP TABLE IF EXISTS {SchemaConstants.TableName} CASCADE;", conn);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger?.LogInformation("Plugin tables dropped successfully.");
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "Failed to drop tables during uninstall.");
    }
}
```

### I4: Service Lifetime Consistency

**Problem**: `UserPreferencesPlugin` registers `IUserPreferenceService` as Scoped while all other infrastructure plugins use Singleton.

**Fix**: Change to `AddSingleton<IUserPreferenceService, UserPreferenceService>()`. Constructor only needs `ITenantConnectionFactory` (Singleton), creates connections per-call internally.

### I5: Dependency Chain Documentation

**Problem**: `OutboxProcessor` depends on `IBrokerAdapter` which is registered in a different assembly. Hidden cross-assembly dependency chain.

**Fix**: Add XML doc comments documenting the dependency chain in `EventOutboxPlugin.RegisterServices()` and `WebhookPlugin.RegisterServices()`.

### D2: Bootstrap Error Handling

**Problem**: All bootstrap failures are swallowed equally. A foundation plugin failure (e.g., EventOutbox) silently breaks all dependents (e.g., Webhooks).

**Fix**: Track failed plugins during bootstrap and skip dependents:

```csharp
var failedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (var feature in topologicalFeatures)
{
    if (feature.DependsOn.Any(d => failedPlugins.Contains(d)))
    {
        _logger.LogWarning("Skipping '{Name}': dependency failed during bootstrap", feature.Name);
        failedPlugins.Add(feature.Name);
        continue;
    }

    try { await InstallAndEnable(feature, ...); }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Plugin '{Name}' failed during bootstrap", feature.Name);
        failedPlugins.Add(feature.Name);
    }
}
```

### D3: ConnectionString Naming

**Problem**: Config key `BmmdlRegistry` is used for both registry and platform operations. Confusing naming.

**Fix**: Add clarifying comment in Program.cs near `ITenantConnectionFactory` registration explaining that registry and platform share the same database connection.

## Execution Plan

All fixes execute in parallel via worktree-isolated agents:

| Agent | Fixes | Key Files |
|-------|-------|-----------|
| **Agent A** | C1, C2, I1, D3 | PlatformFeatureExtensions.cs, Program.cs, PluginDirectoryLoader.cs, new WebhookApiPlugin.cs |
| **Agent B** | C3 (+ I2) | Infrastructure controllers |
| **Agent C** | I3, I4, I5 | 6 infrastructure plugins |
| **Agent D** | D2 | PluginManager.cs |

## Verification

After all fixes:
```bash
dotnet clean BMMDL.sln -q
dotnet build BMMDL.sln
dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj
```

Expected: 0 errors, all tests pass + new tests for each fix.
