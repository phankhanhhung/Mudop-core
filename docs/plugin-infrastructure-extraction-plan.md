# Plugin Infrastructure Extraction Plan

> Created: 2026-03-11
> Status: IN PROGRESS

## Background

The plugin bootstrap system (`BootstrapBuiltInPluginsAsync`) now handles:
- BMMDL module compilation → DDL → schema init for platform entities
- System Tenant seeding (MultiTenancyPlugin)
- Default role seeding (PlatformIdentityPlugin)

However, 8 infrastructure tables still use standalone `EnsureTableAsync()` calls in `Program.cs`,
bypassing the plugin lifecycle entirely. This is inconsistent and prevents these features from
being configurable, disableable, or having admin UIs.

## Audit Summary

### What Plugin System NOW Covers
| Capability | Plugin |
|-----------|--------|
| Tenant entity + DDL + seeding | MultiTenancyPlugin |
| Identity/User/Role/UserRole + DDL + seeding | PlatformIdentityPlugin |
| Tenant isolation (tenant_id, RLS, filters) | MultiTenancyPlugin |
| Audit fields (createdAt/By, updatedAt/By) | AuditFieldFeature |
| Soft delete, inheritance, localization, temporal | Respective feature plugins |
| Sequences, file references, encryption, CDC | Respective feature plugins |
| Admin pages + menu items + settings | Multi-tenancy + Identity plugins |

### What's Still Outside (8 Infrastructure Tables)
| Table | Schema | Service | Program.cs Line |
|-------|--------|---------|----------------|
| `audit_logs` | `platform` | `IAuditLogStore` | 554-557 |
| `event_outbox` | `platform` | `IOutboxStore` | 560-563 |
| `webhook_configs` | `platform` | `IWebhookStore` | 566-569 |
| `webhook_delivery_log` | `platform` | `IWebhookStore` | 566-569 |
| `report_templates` | `platform` | `IReportStore` | 572-575 |
| `comments` | `platform` | `ICommentStore` | 578-583 |
| `change_requests` | `platform` | `IChangeRequestStore` | 578-583 |
| `user_preferences` | `platform` | `IUserPreferenceService` | 586-590 |

### Missing Seed Data
| Data | Legacy Bootstrap | Plugin System |
|------|-----------------|--------------|
| 4 Default Roles | PlatformSeeder.SeedRolesAsync | PlatformIdentityPlugin.OnInstalledAsync ✓ |
| 15 System Permissions | PlatformSeeder.SeedPermissionsAsync | **MISSING** |
| Admin User + Assignment | PlatformSeeder.SeedAdminUserAsync | **MISSING** (requires email/password input) |

---

## Implementation Plan

### Task 1: Permission Seeding (PlatformIdentityPlugin)
**File**: `src/BMMDL.Runtime/Plugins/Features/PlatformIdentityPlugin.cs`

Add `SeedDefaultPermissionsAsync` to `OnInstalledAsync`, seeding 15 system permissions into
`core.role` (or the appropriate permission table) with `ON CONFLICT DO NOTHING`.

Permissions to seed (matching legacy `PlatformSeeder`):
- Platform: `platform:manage`, `platform:read`
- Tenant: `tenant:create`, `tenant:read`, `tenant:update`, `tenant:delete`
- User: `user:create`, `user:read`, `user:update`, `user:delete`
- Role: `role:create`, `role:read`, `role:update`, `role:delete`, `role:assign`

**Note**: Admin user seeding is NOT included — it requires interactive input (email/password)
and should remain in the CLI `bmmdlc bootstrap --seed-admin` command.

**Tests**: Add permission seeding tests to `PlatformIdentityPluginTests.cs`.

---

### Task 2: AuditLoggingPlugin
**File**: `src/BMMDL.Runtime/Plugins/Features/AuditLoggingPlugin.cs`

New plugin implementing:
- `IPlatformFeature` (Name: "AuditLogging", Stage: 80)
- `IPluginLifecycle` (OnInstalledAsync creates `platform.audit_logs` table)
- `ISettingsProvider` (settings: enabled, retentionDays, excludeEntities)
- `IAdminPageProvider` (pages: /admin/audit-logs)
- `IMenuContributor` (menu: "Audit Logs" in main section)

OnInstalledAsync: Move DDL from `PostgresAuditLogStore.EnsureTableAsync()` into the lifecycle hook.
Keep `EnsureTableAsync` on the store but make it a no-op if table already exists (idempotent).

**Tests**: `src/BMMDL.Tests/Plugins/AuditLoggingPluginTests.cs`

---

### Task 3: EventOutboxPlugin
**File**: `src/BMMDL.Runtime/Plugins/Features/EventOutboxPlugin.cs`

New plugin implementing:
- `IPlatformFeature` (Name: "EventOutbox", Stage: 70)
- `IPluginLifecycle` (OnInstalledAsync creates `platform.event_outbox` table)
- `ISettingsProvider` (settings: enabled, batchSize, maxRetries, retryDelayMs, deadLetterRetention)
- `IAdminPageProvider` (pages: /admin/outbox)
- `IMenuContributor` (menu: "Event Outbox" in system section)

**Tests**: `src/BMMDL.Tests/Plugins/EventOutboxPluginTests.cs`

---

### Task 4: WebhookPlugin
**File**: `src/BMMDL.Runtime/Plugins/Features/WebhookPlugin.cs`

New plugin implementing:
- `IPlatformFeature` (Name: "Webhooks", Stage: 85, DependsOn: ["EventOutbox"])
- `IPluginLifecycle` (OnInstalledAsync creates `platform.webhook_configs` + `platform.webhook_delivery_log`)
- `ISettingsProvider` (settings: enabled, maxWebhooksPerTenant, deliveryTimeoutMs, maxRetries)
- `IAdminPageProvider` (pages: /admin/webhooks, /admin/webhooks/create)
- `IMenuContributor` (menu: "Webhooks" in system section)

**Tests**: `src/BMMDL.Tests/Plugins/WebhookPluginTests.cs`

---

### Task 5: CollaborationPlugin
**File**: `src/BMMDL.Runtime/Plugins/Features/CollaborationPlugin.cs`

New plugin implementing:
- `IPlatformFeature` (Name: "Collaboration", Stage: 90)
- `IPluginLifecycle` (OnInstalledAsync creates `platform.comments` + `platform.change_requests`)
- `ISettingsProvider` (settings: enabled, commentsEnabled, changeRequestsEnabled, maxCommentLength)
- `IAdminPageProvider` (pages: /admin/change-requests)
- `IMenuContributor` (menu: "Change Requests" in main section)

**Tests**: `src/BMMDL.Tests/Plugins/CollaborationPluginTests.cs`

---

### Task 6: ReportingPlugin
**File**: `src/BMMDL.Runtime/Plugins/Features/ReportingPlugin.cs`

New plugin implementing:
- `IPlatformFeature` (Name: "Reporting", Stage: 95)
- `IPluginLifecycle` (OnInstalledAsync creates `platform.report_templates`)
- `ISettingsProvider` (settings: enabled, maxTemplatesPerTenant, allowPublicSharing, schedulingEnabled)
- `IAdminPageProvider` (pages: /admin/reports)
- `IMenuContributor` (menu: "Reports" in main section)

**Tests**: `src/BMMDL.Tests/Plugins/ReportingPluginTests.cs`

---

### Task 7: UserPreferencesPlugin
**File**: `src/BMMDL.Runtime/Plugins/Features/UserPreferencesPlugin.cs`

New plugin implementing:
- `IPlatformFeature` (Name: "UserPreferences", Stage: 100)
- `IPluginLifecycle` (OnInstalledAsync creates `platform.user_preferences`)
- `ISettingsProvider` (settings: enabled, maxPreferencesPerUser)

No admin pages or menu items (user preferences are per-user, not admin-managed).

**Tests**: `src/BMMDL.Tests/Plugins/UserPreferencesPluginTests.cs`

---

### Task 8: Program.cs Cleanup
**File**: `src/BMMDL.Runtime.Api/Program.cs`

After all plugins are created and merged:
1. Remove 7 standalone `EnsureTableAsync`/`EnsureTablesAsync` blocks (lines 554-590)
2. The plugin bootstrap block (lines 592-601) handles table creation via `OnInstalledAsync`
3. Verify build + all tests pass

---

## Architecture Notes

### Why NOT use IBmmdlModuleProvider for infrastructure tables?
Infrastructure tables (`audit_logs`, `event_outbox`, etc.) are NOT business entities:
- They use PostgreSQL-specific features (JSONB, specific index types)
- They don't need OData access, metadata publishing, or tenant isolation
- They have a chicken-and-egg problem (outbox must exist before modules compile)

Instead, each plugin uses `IPluginLifecycle.OnInstalledAsync` with raw `CREATE TABLE IF NOT EXISTS` DDL.
This is consistent with how MultiTenancyPlugin seeds the System Tenant.

### Plugin table creation order
The `BootstrapBuiltInPluginsAsync` iterates features in topological order (by Stage + DependsOn).
Infrastructure plugins use high Stage numbers (70-100) to run after core plugins:
- Stage -1: MultiTenancyPlugin (tenant isolation)
- Stage 0: PlatformIdentityPlugin (identity/auth)
- Stage 70: EventOutboxPlugin
- Stage 80: AuditLoggingPlugin
- Stage 85: WebhookPlugin (depends on EventOutbox)
- Stage 90: CollaborationPlugin
- Stage 95: ReportingPlugin
- Stage 100: UserPreferencesPlugin

### DI remains unchanged
The stores (`IAuditLogStore`, `IOutboxStore`, etc.) remain registered as singletons in DI.
The plugin is a thin lifecycle wrapper — it controls table creation and provides settings/pages,
but the actual store implementations are unchanged.

## Success Criteria
- All 8 infrastructure tables created via plugin `OnInstalledAsync` instead of standalone `EnsureTableAsync`
- 7 standalone init blocks removed from `Program.cs`
- Permission seeding added to PlatformIdentityPlugin
- All existing tests pass (2878+)
- New plugin tests added for each infrastructure plugin
- Zero regressions in E2E tests
