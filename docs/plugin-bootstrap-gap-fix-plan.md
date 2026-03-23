# Plugin Bootstrap Gap Fix Plan

> Created: 2026-03-11
> Status: DONE — All gaps G1-G5 fixed, 2878 tests passing
> Successor: `docs/plugin-infrastructure-extraction-plan.md` (infrastructure plugins)

## Context

The plugin architecture (Phase 1-6) is complete: interfaces, 10 built-in features, contribution pass,
runtime DLL loading, and legacy fallback cleanup. However, the plugin installation flow does NOT yet
fully replace the legacy CLI `bootstrap` command. This plan closes those gaps.

## Gap Analysis

### G1: `EnsurePluginTablesAsync` not called at startup [CRITICAL]

**Problem**: `core.plugin_state` and `core.plugin_migration_history` tables don't exist until
`PluginManager.EnsurePluginTablesAsync()` is explicitly called — but nobody calls it during startup.
Any plugin lifecycle operation (install/enable/disable) will crash with a missing table error.

**Fix**: Call `EnsurePluginTablesAsync()` in `Program.cs` startup section, right after existing
infrastructure table initialization.

**Files**:
- `src/BMMDL.Runtime.Api/Program.cs` — add startup call

### G2: Built-in plugins not auto-installed at startup [CRITICAL]

**Problem**: MultiTenancyPlugin and PlatformIdentityPlugin are registered as features in DI,
but never installed (BMMDL modules not compiled, entity tables not created) or enabled
(global mode not activated for MultiTenancy). The user must manually call
`POST /api/plugins/{name}/install` + `POST /api/plugins/{name}/enable` for each — this defeats
the purpose of built-in plugins.

**Fix**: Add auto-bootstrap loop in `Program.cs` after `EnsurePluginTablesAsync`:
```
For each registered feature (topological order from registry):
  state = GetPluginState(feature.Name)
  if state is null → InstallPluginAsync → EnablePluginAsync
  if state.Status == Installed → EnablePluginAsync
  if state.Status == Disabled → skip (user explicitly disabled)
```

**Important**: Must handle first-boot gracefully — Registry API might not be running yet when
Runtime API starts. Module installation (compile BMMDL → create tables) requires Registry API.
Solution: catch and log warnings, retry on next restart. The fallback entity cache (G3) covers
the gap until modules are installed.

**Files**:
- `src/BMMDL.Runtime.Api/Program.cs` — add auto-bootstrap loop
- `src/BMMDL.Runtime/Plugins/PluginManager.cs` — add `BootstrapBuiltInPluginsAsync` method

### G3: IPlatformEntityProvider fallback not loaded into MetaModelCache [HIGH]

**Problem**: When registry DB is empty (first boot, or modules not yet compiled),
`MetaModelCacheManager.LoadCacheAsync()` returns an empty cache (0 entities). The system
has `IPlatformEntityProvider` implementations that return entity definitions for exactly
this scenario — but `MetaModelCacheManager` never calls them.

**Fix**: In `MetaModelCacheManager.LoadCacheAsync()`, if the loaded model has 0 entities,
fall back to `PlatformFeatureRegistry.GetAllPlatformEntities()` to populate a bootstrap cache.
This requires injecting `PlatformFeatureRegistry` into `MetaModelCacheManager`.

**Files**:
- `src/BMMDL.Runtime/MetaModelCacheManager.cs` — add fallback logic
- `src/BMMDL.Runtime.Api/Program.cs` — pass registry to MetaModelCacheManager

### G4: No System Tenant seeding [HIGH]

**Problem**: Legacy bootstrap creates System Tenant (Guid.Empty, code="system", name="System Tenant")
in the platform.tenant table. Without it, system-level operations that reference tenant have no row.

**Fix**: Handle in `MultiTenancyPlugin.OnInstalledAsync()` — when the plugin is installed,
seed the System Tenant row if it doesn't exist. This is the natural owner of that responsibility.

**Files**:
- `src/BMMDL.Runtime/Plugins/Features/MultiTenancyPlugin.cs` — seed in OnInstalledAsync

### G5: No admin user/role seeding [MEDIUM]

**Problem**: Legacy bootstrap seeds default roles (SuperAdmin, TenantAdmin, User, Guest) and
an admin user. Plugin flow doesn't seed any of this.

**Fix**: Handle in `PlatformIdentityPlugin.OnInstalledAsync()` — seed default roles when
plugin is installed. Admin user creation should remain a separate manual step (security).

**Files**:
- `src/BMMDL.Runtime/Plugins/Features/PlatformIdentityPlugin.cs` — seed default roles in OnInstalledAsync

### G6: Infrastructure tables outside plugin system [INFO — FUTURE]

**Problem**: 7 infrastructure tables are created via `EnsureTableAsync()` directly in Program.cs,
outside the plugin architecture:

| Table | Service | Future Plugin |
|-------|---------|---------------|
| `platform.event_outbox` | OutboxStore | EventOutboxPlugin |
| `platform.audit_logs` | AuditLogStore | AuditLogPlugin |
| `platform.webhook_configs` | WebhookStore | WebhookPlugin |
| `platform.webhook_delivery_log` | WebhookStore | WebhookPlugin |
| `platform.report_templates` | ReportStore | ReportingPlugin |
| `platform.comments` | CommentStore | CollaborationPlugin |
| `platform.change_requests` | ChangeRequestStore | CollaborationPlugin |
| `platform.user_preferences` | UserPreferenceService | UserPreferencesPlugin |

**Status**: Working correctly today via `EnsureTableAsync()`. No action needed now.
These can be migrated to plugins in a future phase when the system matures.

## Implementation Tasks

### Task 1: Plugin Bootstrap in Program.cs (G1 + G2)

Add to `Program.cs` startup section (after existing infrastructure table initialization):

```csharp
// Ensure plugin state tables exist
{
    var pluginManager = app.Services.GetRequiredService<IPluginManager>() as PluginManager;
    if (pluginManager is not null)
    {
        await pluginManager.EnsurePluginTablesAsync();
        await pluginManager.BootstrapBuiltInPluginsAsync();
    }
}
```

Add `BootstrapBuiltInPluginsAsync` to `PluginManager`:
- Iterates `PlatformFeatureRegistry.AllFeatures` in topological order
- For each: check state → install if missing → enable if installed
- Wraps module installation in try/catch (Registry API may not be available)
- Logs clear status for each plugin

**Tests**:
- `PluginManagerTests.BootstrapBuiltInPlugins_FirstBoot_InstallsAndEnablesAll`
- `PluginManagerTests.BootstrapBuiltInPlugins_AlreadyEnabled_Skips`
- `PluginManagerTests.BootstrapBuiltInPlugins_ExplicitlyDisabled_DoesNotReEnable`
- `PluginManagerTests.BootstrapBuiltInPlugins_RegistryUnavailable_LogsWarningContinues`

### Task 2: MetaModelCache Fallback (G3)

Modify `MetaModelCacheManager` to accept `PlatformFeatureRegistry` (optional).
In `LoadCacheAsync()`, if model has 0 entities, build a bootstrap model from
`registry.GetAllPlatformEntities()`.

**Tests**:
- `MetaModelCacheManagerTests.LoadCache_EmptyRegistry_FallsBackToPlatformEntities`
- `MetaModelCacheManagerTests.LoadCache_RegistryHasEntities_NoFallback`
- `MetaModelCacheManagerTests.LoadCache_NoRegistryInjected_ReturnsEmptyCache`

### Task 3: System Tenant Seeding (G4)

In `MultiTenancyPlugin.OnInstalledAsync()`:
- Get `ITenantConnectionFactory` from `PluginContext.Services`
- Check if System Tenant exists (SELECT WHERE id = '00000000-...')
- If not: INSERT System Tenant row (id=Guid.Empty, code='system', name='System Tenant')

**Tests**:
- `MultiTenancyPluginTests.OnInstalled_SeedsSystemTenant` (integration test pattern)
- Unit test with mock connection factory

### Task 4: Default Role Seeding (G5)

In `PlatformIdentityPlugin.OnInstalledAsync()`:
- Get `ITenantConnectionFactory` from `PluginContext.Services`
- Seed default roles: SuperAdmin, TenantAdmin, User, Guest
- Use INSERT ... ON CONFLICT DO NOTHING for idempotency

**Tests**:
- `PlatformIdentityPluginTests.OnInstalled_SeedsDefaultRoles` (integration test pattern)
- Unit test with mock connection factory

## Execution Order

Tasks 1-4 can be implemented in parallel. Each task is self-contained with clear boundaries.

## Success Criteria

After all tasks are complete:
1. Fresh Runtime API startup with empty database → plugin tables created, built-in plugins
   auto-installed and enabled, MetaModelCache has fallback entities
2. Subsequent startups → no-op (idempotent)
3. User disables a plugin → stays disabled across restarts
4. Registry API unavailable at first boot → graceful degradation with warnings, retry on next restart
5. All existing tests pass (zero regressions)
6. New tests cover all gap scenarios
