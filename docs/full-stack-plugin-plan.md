# Full-Stack Plugin Architecture — Implementation Plan

> **Date**: 2026-03-09
> **Status**: Phase 5-6 DONE, Phase 7 IN PROGRESS (infrastructure extraction)
> **Predecessor**: `docs/plugin-architecture-plan.md` (Phase 1–4 DONE)
> **Goal**: Evolve plugins from "DDL/DML hooks" into self-contained full-stack modules
> that can define platform entities, backend APIs, admin UI, settings, and migrations.

---

## 1. Vision

A **plugin** is a self-contained package that can contribute across ALL layers:

```
┌──────────────────────────────────────────────────────────────────────┐
│  Plugin: MultiTenancy                                                │
│                                                                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌────────────┐  │
│  │  Platform     │ │  DDL/DML     │ │  Admin API   │ │  Admin UI  │  │
│  │  Entities     │ │  Features    │ │  Endpoints   │ │  Pages     │  │
│  │              │ │              │ │              │ │            │  │
│  │  Tenant      │ │  tenant_id   │ │  POST /api/  │ │  Tenant    │  │
│  │  TenantUser  │ │  WHERE filter│ │    tenants   │ │  List      │  │
│  │              │ │  RLS policy  │ │  PUT /api/   │ │  Create    │  │
│  │              │ │  INSERT col  │ │    tenants/  │ │  Settings  │  │
│  └──────────────┘ └──────────────┘ └──────────────┘ └────────────┘  │
│                                                                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐                  │
│  │  Middleware   │ │  Settings    │ │  Migrations  │                  │
│  │              │ │              │ │              │                  │
│  │  Tenant      │ │  enabled     │ │  001_create  │                  │
│  │  Context     │ │  maxTenants  │ │  _tables.sql │                  │
│  │  Extraction  │ │  defaultTier │ │              │                  │
│  └──────────────┘ └──────────────┘ └──────────────┘                  │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 2. What Already Exists (Phase 1–4 Done)

| Layer | What | Status |
|-------|------|--------|
| MetaModel | `BmFeatureMetadata`, `FeatureTypes` records | DONE |
| Runtime | `IPlatformFeature`, 6 capability interfaces | DONE |
| Runtime | `PlatformFeatureRegistry` (topological sort) | DONE |
| Runtime | `FeatureFilterState` (scoped enable/disable) | DONE |
| Runtime | 10 DDL/DML features (TenantIsolation, SoftDelete, etc.) | DONE |
| Runtime | Full-stack interfaces (IPlatformEntityProvider, IAdminPageProvider, etc.) | DONE (Phase 5) |
| Runtime | MultiTenancyPlugin + PlatformIdentityPlugin (full-stack) | DONE (Phase 5) |
| Runtime | Plugin bootstrap (`BootstrapBuiltInPluginsAsync`) | DONE (Phase 6) |
| Runtime | MetaModelCacheManager fallback to PlatformEntityProvider | DONE (Phase 6) |
| Runtime | System Tenant seeding + Default role seeding | DONE (Phase 6) |
| Runtime | Runtime DLL loading (`PluginDirectoryLoader`) | DONE (Phase 5) |
| Runtime | Legacy fallback cleanup (all behavior through plugin pipeline) | DONE (Phase 6) |
| Runtime | Infrastructure plugins (AuditLogging, EventOutbox, Webhooks, etc.) | IN PROGRESS (Phase 7) |
| Runtime.Api | `IEntityOperationBehavior` + `ApiFeatureRegistry` | DONE |
| Runtime.Api | `ETagFeature` (API pipeline behavior) | DONE |
| Runtime.Api | Plugin API endpoints (scan, load, unload, manifest) | DONE (Phase 5) |
| Compiler | `FeatureContributionPass` (Order 61) | DONE |
| Frontend | Plugin registry, dynamic routing, management UI | DONE (Phase 5) |
| Tests | 2878+ unit tests, 656 E2E tests passing | DONE |

**Current focus**: Phase 7 — extracting 8 infrastructure services (outbox, webhooks, audit, etc.) into proper plugins with lifecycle management, settings, and admin pages.

---

## 3. New Capability Interfaces

### 3.1 Interface Hierarchy (new interfaces marked with *)

```
IPlatformFeature (base — exists)
│
│  ── COMPILE TIME ──
├── IFeatureMetadataContributor         (exists) DDL/DML metadata enrichment
│
│  ── RUNTIME DML ──
├── IFeatureQueryFilter                 (exists) SELECT WHERE waterfall
├── IFeatureInsertContributor           (exists) INSERT column waterfall
├── IFeatureUpdateContributor           (exists) UPDATE SET waterfall
├── IFeatureDeleteStrategy              (exists) DELETE bail hook
├── IFeatureWriteHook                   (exists) Before/after lifecycle tap
│
│  ── RUNTIME API ──
├── IEntityOperationBehavior            (exists) MediatR-style CRUD pipeline
│
│  ── NEW: FULL-STACK ──
├── *IPlatformEntityProvider            Platform table definitions
├── *IAdminApiProvider                  Admin API controllers/endpoints
├── *IMiddlewareProvider                HTTP middleware registration
├── *IAdminPageProvider                 Frontend admin pages/routes
├── *IMenuContributor                   Sidebar menu items
├── *ISettingsProvider                  Plugin configuration schema
├── *IMigrationProvider                 Schema migrations for plugin tables
└── *IPluginLifecycle                   Install/enable/disable/uninstall hooks
```

### 3.2 Interface Definitions

```csharp
// ── Platform Entities ──

/// Provides platform table definitions (e.g., platform.tenant, core.audit_entry).
/// Tables are created during plugin installation via IMigrationProvider.
/// Entity definitions are registered into MetaModelCache for runtime CRUD.
public interface IPlatformEntityProvider : IPlatformFeature
{
    /// Returns BmEntity definitions for platform tables this plugin needs.
    /// These are NOT user-defined entities — they are platform infrastructure.
    IReadOnlyList<BmEntity> GetPlatformEntities();
}

// ── Admin API ──

/// Provides admin API endpoint definitions.
/// Endpoints are registered during app startup via minimal API or controller discovery.
public interface IAdminApiProvider : IPlatformFeature
{
    /// Register endpoints on the given route group.
    /// Example: group.MapPost("/tenants", CreateTenant);
    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// Register services needed by this plugin's endpoints.
    /// Example: services.AddScoped<ITenantService, DynamicPlatformTenantService>();
    void RegisterServices(IServiceCollection services);
}

// ── Middleware ──

/// Provides HTTP middleware that runs in the request pipeline.
/// Order controlled by IPlatformFeature.Stage.
public interface IMiddlewareProvider : IPlatformFeature
{
    /// Configure middleware on the application builder.
    /// Called during app startup in dependency order.
    void UseMiddleware(IApplicationBuilder app);
}

// ── Admin UI (Frontend) ──

/// Provides admin UI page definitions consumed by the Vue frontend.
/// The backend serves these as a manifest; the frontend renders them.
public interface IAdminPageProvider : IPlatformFeature
{
    /// Returns page definitions for the admin UI.
    IReadOnlyList<PluginPageDefinition> GetPages();
}

public record PluginPageDefinition(
    string Route,           // e.g., "/admin/tenants"
    string Title,           // e.g., "Tenant Management"
    string Component,       // Vue component name, e.g., "TenantListPage"
    string? Icon = null,    // Lucide icon name
    string? ParentRoute = null,  // For nested routes
    Dictionary<string, object>? Meta = null  // Route meta
);

// ── Menu Items ──

/// Contributes items to the admin sidebar menu.
public interface IMenuContributor : IPlatformFeature
{
    IReadOnlyList<PluginMenuItem> GetMenuItems();
}

public record PluginMenuItem(
    string Label,           // e.g., "Tenants"
    string Route,           // e.g., "/admin/tenants"
    string Icon,            // Lucide icon name
    string Section,         // "main" | "admin" | "tools"
    int Order = 100,        // Sort order within section
    string? Badge = null,   // Optional badge text
    string? RequiredPermission = null  // Permission check
);

// ── Settings ──

/// Defines configurable settings for this plugin.
/// Settings are stored in core.plugin_settings table.
/// Admin UI renders a settings form based on the schema.
public interface ISettingsProvider : IPlatformFeature
{
    PluginSettingsSchema GetSettingsSchema();
    /// Called when settings change. Plugin can validate + react.
    Task OnSettingsChangedAsync(Dictionary<string, object?> newSettings, CancellationToken ct);
}

public record PluginSettingsSchema(
    string GroupLabel,      // "Multi-Tenancy Settings"
    IReadOnlyList<PluginSetting> Settings
);

public record PluginSetting(
    string Key,             // "maxTenants"
    string Label,           // "Maximum Tenants"
    string Type,            // "boolean" | "integer" | "string" | "select"
    object? DefaultValue,   // 100
    bool Required = false,
    string? Description = null,
    string[]? Options = null  // For "select" type
);

// ── Migrations ──

/// Provides schema migrations for plugin-owned tables.
/// Migrations run during plugin install/upgrade.
public interface IMigrationProvider : IPlatformFeature
{
    IReadOnlyList<PluginMigration> GetMigrations();
}

public record PluginMigration(
    int Version,            // Sequential: 1, 2, 3...
    string Description,     // "Create tenant tables"
    string UpSql,           // SQL to apply
    string DownSql          // SQL to revert
);

// ── Lifecycle ──

/// Hooks for plugin install/enable/disable/uninstall.
/// Allows plugins to perform setup/teardown beyond schema migrations.
public interface IPluginLifecycle : IPlatformFeature
{
    Task OnInstalledAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnEnabledAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnDisabledAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;
    Task OnUninstalledAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;
}

public class PluginContext
{
    public IServiceProvider Services { get; init; }
    public Dictionary<string, object?> Settings { get; init; }
}
```

---

## 4. Plugin Registry & State Management

### 4.1 Plugin State Table

```sql
-- New table: core.plugin_state
CREATE TABLE core.plugin_state (
    name          VARCHAR(100) PRIMARY KEY,
    version       INTEGER NOT NULL DEFAULT 1,
    status        VARCHAR(20) NOT NULL DEFAULT 'installed',  -- installed | enabled | disabled
    installed_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    enabled_at    TIMESTAMPTZ,
    settings_json JSONB DEFAULT '{}',
    CHECK (status IN ('installed', 'enabled', 'disabled'))
);

-- New table: core.plugin_migration_history
CREATE TABLE core.plugin_migration_history (
    plugin_name   VARCHAR(100) NOT NULL,
    version       INTEGER NOT NULL,
    description   VARCHAR(500),
    applied_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    checksum      VARCHAR(64),
    PRIMARY KEY (plugin_name, version)
);
```

### 4.2 Extended PlatformFeatureRegistry

```
PlatformFeatureRegistry (existing — extend)
│
├── All existing typed lists (MetadataContributors, QueryFilters, etc.)
│
├── NEW: PlatformEntityProviders    → IReadOnlyList<IPlatformEntityProvider>
├── NEW: AdminApiProviders          → IReadOnlyList<IAdminApiProvider>
├── NEW: MiddlewareProviders        → IReadOnlyList<IMiddlewareProvider>
├── NEW: AdminPageProviders         → IReadOnlyList<IAdminPageProvider>
├── NEW: MenuContributors           → IReadOnlyList<IMenuContributor>
├── NEW: SettingsProviders          → IReadOnlyList<ISettingsProvider>
├── NEW: MigrationProviders         → IReadOnlyList<IMigrationProvider>
├── NEW: LifecycleHooks             → IReadOnlyList<IPluginLifecycle>
│
├── NEW: GetPluginState(name)       → PluginState (from DB)
├── NEW: IsPluginEnabled(name)      → bool
├── NEW: GetAllPluginManifests()    → for admin UI /api/plugins endpoint
└── NEW: GetAggregatedMenuItems()   → merged + sorted menu items
```

### 4.3 Plugin-Aware Startup Flow

```
Application Startup
│
├── 1. Load all IPlatformFeature implementations (DI scan or explicit registration)
├── 2. Build PlatformFeatureRegistry (topological sort — existing)
├── 3. Read core.plugin_state table → determine which plugins are enabled
├── 4. For enabled plugins:
│   ├── 4a. IMigrationProvider.GetMigrations() → run pending migrations
│   ├── 4b. IPlatformEntityProvider.GetPlatformEntities() → register in MetaModelCache
│   ├── 4c. IAdminApiProvider.RegisterServices() → add to DI
│   ├── 4d. IAdminApiProvider.MapEndpoints() → register routes
│   ├── 4e. IMiddlewareProvider.UseMiddleware() → add to pipeline
│   └── 4f. IPluginLifecycle.OnEnabledAsync() → plugin-specific setup
├── 5. For disabled plugins: skip all of the above
└── 6. Serve /api/plugins/manifest → aggregated pages + menu items for frontend
```

---

## 5. Admin Plugin Management API

```
GET    /api/plugins                    → List all plugins with state
GET    /api/plugins/{name}             → Plugin detail (state, settings, migrations)
POST   /api/plugins/{name}/install     → Run migrations, set status=installed
POST   /api/plugins/{name}/enable      → Set status=enabled, call OnEnabledAsync
POST   /api/plugins/{name}/disable     → Set status=disabled, call OnDisabledAsync
DELETE /api/plugins/{name}             → Run down-migrations, call OnUninstalledAsync
PUT    /api/plugins/{name}/settings    → Update plugin settings
GET    /api/plugins/manifest           → Aggregated menu items + pages for frontend
```

**Controller**: `src/BMMDL.Runtime.Api/Controllers/PluginController.cs`

**Service**: `src/BMMDL.Runtime/Plugins/PluginManager.cs`
- Reads/writes `core.plugin_state` and `core.plugin_migration_history`
- Executes migrations via `IMigrationProvider`
- Calls lifecycle hooks
- Validates dependencies (can't disable plugin X if plugin Y depends on it)

---

## 6. Frontend Plugin System

### 6.1 Plugin Manifest Endpoint

Backend serves: `GET /api/plugins/manifest`

```json
{
  "plugins": [
    {
      "name": "MultiTenancy",
      "status": "enabled",
      "menuItems": [
        {
          "label": "Tenants",
          "route": "/admin/tenants",
          "icon": "Users",
          "section": "admin",
          "order": 10
        }
      ],
      "pages": [
        {
          "route": "/admin/tenants",
          "title": "Tenant Management",
          "component": "PluginTenantList"
        },
        {
          "route": "/admin/tenants/create",
          "title": "Create Tenant",
          "component": "PluginTenantCreate"
        }
      ],
      "settings": {
        "groupLabel": "Multi-Tenancy Settings",
        "values": { "enabled": true, "maxTenants": 100 }
      }
    }
  ]
}
```

### 6.2 Frontend Architecture

```
frontend/src/
├── plugins/
│   ├── pluginRegistry.ts          # Fetches /api/plugins/manifest, caches result
│   ├── pluginRouter.ts            # Generates Vue Router routes from manifest
│   ├── pluginMenu.ts              # Merges plugin menu items with core items
│   └── components/                # Plugin-provided Vue components
│       ├── index.ts               # Component registry (lazy-loaded map)
│       ├── PluginTenantList.vue
│       ├── PluginTenantCreate.vue
│       ├── PluginAuditLogViewer.vue
│       ├── PluginTrashBin.vue
│       └── PluginSettingsForm.vue # Generic settings form (renders from schema)
│
├── views/admin/
│   └── PluginManagementView.vue   # Install/enable/disable/uninstall plugins
│
├── router/index.ts                # MODIFIED: merge core routes + plugin routes
├── components/common/
│   └── AppSidebar.vue             # MODIFIED: merge core menu + plugin menu items
```

### 6.3 Plugin Component Registry

Two approaches for loading plugin Vue components:

**Approach A: Built-in components (Phase 1 — simpler)**
All plugin UI components are bundled in the frontend. Manifest maps component names
to lazy-loaded imports:

```typescript
// frontend/src/plugins/components/index.ts
const pluginComponents: Record<string, () => Promise<Component>> = {
  'PluginTenantList':     () => import('./PluginTenantList.vue'),
  'PluginTenantCreate':   () => import('./PluginTenantCreate.vue'),
  'PluginAuditLogViewer': () => import('./PluginAuditLogViewer.vue'),
  'PluginTrashBin':       () => import('./PluginTrashBin.vue'),
  'PluginSettingsForm':   () => import('./PluginSettingsForm.vue'),
}
```

**Approach B: Dynamic loading (Phase 2 — future)**
Plugin components served from backend as ES modules, loaded via dynamic import URL.
This enables true runtime extensibility without rebuilding the frontend.

We start with Approach A — it's sufficient for built-in plugins and avoids the
complexity of remote module loading.

### 6.4 Dynamic Router Integration

```typescript
// frontend/src/plugins/pluginRouter.ts
import { pluginComponents } from './components'

export function generatePluginRoutes(manifest: PluginManifest): RouteRecordRaw[] {
  return manifest.plugins
    .filter(p => p.status === 'enabled')
    .flatMap(p => p.pages.map(page => ({
      path: page.route,
      name: `plugin-${p.name}-${page.route}`,
      component: pluginComponents[page.component]
        ?? (() => import('../views/admin/PluginFallbackView.vue')),
      meta: {
        title: page.title,
        requiresAuth: true,
        plugin: p.name,
        ...page.meta
      }
    })))
}

// In router/index.ts:
const pluginRoutes = generatePluginRoutes(await fetchPluginManifest())
router.addRoute({ path: '/', children: pluginRoutes })
```

### 6.5 Dynamic Sidebar Integration

```typescript
// frontend/src/plugins/pluginMenu.ts
export function mergeMenuItems(
  coreItems: MenuItem[],
  manifest: PluginManifest
): MenuItem[] {
  const pluginItems = manifest.plugins
    .filter(p => p.status === 'enabled')
    .flatMap(p => p.menuItems)
    .sort((a, b) => a.order - b.order)

  // Group by section, merge with core items
  return [...coreItems, ...pluginItems]
    .sort((a, b) => a.order - b.order)
}
```

---

## 7. Example: MultiTenancy Plugin (Complete)

To show how all pieces fit together, here's the full MultiTenancy plugin:

```csharp
public class MultiTenancyPlugin :
    IPlatformFeature,
    // Compile-time
    IFeatureMetadataContributor,
    // Runtime DML
    IFeatureQueryFilter,
    IFeatureInsertContributor,
    IFeatureUpdateContributor,
    // NEW: Full-stack
    IPlatformEntityProvider,
    IAdminApiProvider,
    IMiddlewareProvider,
    IAdminPageProvider,
    IMenuContributor,
    ISettingsProvider,
    IMigrationProvider,
    IPluginLifecycle
{
    public string Name => "MultiTenancy";
    public IReadOnlyList<string> DependsOn => [];
    public int Stage => 0;

    public bool AppliesTo(BmEntity entity)
        => entity.TenantScoped;

    // ── Existing: DDL metadata (unchanged from TenantIsolationFeature) ──
    public void ContributeMetadata(BmEntity entity, FeatureContributionContext ctx) { ... }
    public QueryFilterContext ApplyFilter(BmEntity entity, QueryFilterContext ctx) { ... }
    public InsertContext ContributeInsert(BmEntity entity, InsertContext ctx) { ... }
    public UpdateContext ContributeUpdate(BmEntity entity, UpdateContext ctx) { ... }

    // ── NEW: Platform Entities ──
    public IReadOnlyList<BmEntity> GetPlatformEntities() =>
    [
        BuildTenantEntity(),      // platform.tenant
        BuildTenantUserEntity(),  // core.tenant_user (junction)
    ];

    // ── NEW: Admin API ──
    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IPlatformTenantService, DynamicPlatformTenantService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tenants").RequireAuthorization();
        group.MapGet("/", TenantEndpoints.ListTenants);
        group.MapPost("/", TenantEndpoints.CreateTenant);
        group.MapGet("/{id:guid}", TenantEndpoints.GetTenant);
        group.MapPut("/{id:guid}", TenantEndpoints.UpdateTenant);
        group.MapPost("/{id:guid}/switch", TenantEndpoints.SwitchTenant);
        group.MapPost("/{id:guid}/invite", TenantEndpoints.InviteUser);
    }

    // ── NEW: Middleware ──
    public void UseMiddleware(IApplicationBuilder app)
    {
        app.UseMiddleware<TenantContextMiddleware>();
    }

    // ── NEW: Admin UI Pages ──
    public IReadOnlyList<PluginPageDefinition> GetPages() =>
    [
        new("/admin/tenants",        "Tenant Management", "PluginTenantList",   "Users"),
        new("/admin/tenants/create", "Create Tenant",     "PluginTenantCreate", "UserPlus"),
    ];

    // ── NEW: Menu Items ──
    public IReadOnlyList<PluginMenuItem> GetMenuItems() =>
    [
        new("Tenants", "/admin/tenants", "Users", "main", Order: 30),
    ];

    // ── NEW: Settings ──
    public PluginSettingsSchema GetSettingsSchema() => new(
        "Multi-Tenancy Settings",
        [
            new("enabled",       "Enable Multi-Tenancy",  "boolean", true,  Required: true),
            new("maxTenants",    "Maximum Tenants",        "integer", 100),
            new("defaultTier",   "Default Subscription",   "select",  "free",
                Options: ["free", "standard", "premium"]),
            new("allowSelfServe","Allow Self-Service Create","boolean", true),
        ]);

    public Task OnSettingsChangedAsync(Dictionary<string, object?> settings, CancellationToken ct)
    {
        // Validate, e.g., maxTenants >= current tenant count
        return Task.CompletedTask;
    }

    // ── NEW: Migrations ──
    public IReadOnlyList<PluginMigration> GetMigrations() =>
    [
        new(1, "Create tenant tables", UpSql: """
            CREATE TABLE IF NOT EXISTS platform.tenant (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                code VARCHAR(100) NOT NULL UNIQUE,
                name VARCHAR(255) NOT NULL,
                description TEXT,
                owner_identity_id UUID NOT NULL,
                subscription_tier VARCHAR(50) DEFAULT 'free',
                max_users INTEGER DEFAULT 10,
                is_active BOOLEAN DEFAULT true,
                created_at TIMESTAMPTZ DEFAULT now(),
                updated_at TIMESTAMPTZ
            );
            CREATE INDEX idx_tenant_code ON platform.tenant(code);
            CREATE INDEX idx_tenant_owner ON platform.tenant(owner_identity_id);
            """,
            DownSql: "DROP TABLE IF EXISTS platform.tenant CASCADE;"),

        new(2, "Enable RLS on tenant table", UpSql: """
            ALTER TABLE platform.tenant ENABLE ROW LEVEL SECURITY;
            """,
            DownSql: """
            ALTER TABLE platform.tenant DISABLE ROW LEVEL SECURITY;
            """),
    ];

    // ── NEW: Lifecycle ──
    public async Task OnInstalledAsync(PluginContext ctx, CancellationToken ct)
    {
        // Create default "system" tenant if none exists
    }

    public async Task OnDisabledAsync(PluginContext ctx, CancellationToken ct)
    {
        // Warn if tenants exist — data won't be accessible
    }
}
```

---

## 8. Additional Plugin Examples

### 8.1 AuditLog Plugin

| Capability | Contribution |
|-----------|-------------|
| `IPlatformEntityProvider` | `core.audit_entry` table (entity, action, user, timestamp, changes_json) |
| `IFeatureWriteHook` | OnAfterInsert/Update/Delete → log to audit_entry |
| `IAdminApiProvider` | `GET /api/audit?entity=X&from=Y&to=Z` |
| `IAdminPageProvider` | AuditLogViewer page with filters + timeline |
| `IMenuContributor` | "Audit Log" in admin section |
| `ISettingsProvider` | retentionDays, excludedEntities[], logLevel |
| `IMigrationProvider` | Create audit_entry table + indexes |

### 8.2 SoftDelete Plugin

| Capability | Contribution |
|-----------|-------------|
| `IFeatureMetadataContributor` | `is_deleted` BOOLEAN column + `deleted_at` + `deleted_by` |
| `IFeatureQueryFilter` | WHERE is_deleted = false |
| `IFeatureDeleteStrategy` | Convert DELETE → UPDATE is_deleted = true |
| `IPlatformEntityProvider` | (none — uses entity columns, not separate table) |
| `IAdminApiProvider` | `POST /api/{entity}/{id}/restore`, `GET /api/trash` |
| `IAdminPageProvider` | Trash Bin page with restore/purge actions |
| `IMenuContributor` | "Trash Bin" in tools section |
| `ISettingsProvider` | autoDeleteDays, excludedEntities[] |

### 8.3 Localization Plugin

| Capability | Contribution |
|-----------|-------------|
| `IFeatureMetadataContributor` | _texts companion tables, locale-based LEFT JOIN |
| `IFeatureQueryFilter` | JOIN with _texts for localized fields |
| `IPlatformEntityProvider` | `core.language` table |
| `IAdminApiProvider` | `GET/POST /api/languages`, `GET/POST /api/translations` |
| `IAdminPageProvider` | Language Manager, Translation Editor |
| `IMenuContributor` | "Languages" in admin section |
| `ISettingsProvider` | defaultLocale, fallbackLocale, supportedLocales[] |

---

## 9. Implementation Phases

### Phase 5: Plugin Manager + State Persistence
**Goal**: Plugins can be installed/enabled/disabled with state persisted in DB.

**New files**:
```
src/BMMDL.Runtime/Plugins/
  PluginManager.cs                    # Install/enable/disable/uninstall logic
  PluginState.cs                      # State enum + record
  IPluginLifecycle.cs                 # Lifecycle hooks interface
  IMigrationProvider.cs               # Migration interface

src/BMMDL.Runtime.Api/Controllers/
  PluginController.cs                 # Admin API for plugin management

src/BMMDL.Tests/Plugins/
  PluginManagerTests.cs               # State transitions, dependency validation
  PluginMigrationTests.cs             # Migration execution, rollback
```

**Changes to existing**:
- `PlatformFeatureRegistry.cs` — add plugin state awareness (skip disabled plugins)
- `SchemaConstants.cs` — add `PluginStateTable`, `PluginMigrationTable` constants
- `Program.cs` — load plugin states at startup

**Database**:
- `core.plugin_state` table
- `core.plugin_migration_history` table

**Tests**: ~30 (state machine, dependency validation, migration runner)

---

### Phase 6: Platform Entity Provider + Admin API Provider
**Goal**: Plugins can define platform tables and register API endpoints.

**New files**:
```
src/BMMDL.Runtime/Plugins/
  IPlatformEntityProvider.cs          # Interface
  IAdminApiProvider.cs                # Interface

src/BMMDL.Runtime/Plugins/Features/
  MultiTenancyPlugin.cs              # Refactored from TenantIsolationFeature
                                      # + TenantController + TenantService

src/BMMDL.Runtime.Api/Endpoints/
  TenantEndpoints.cs                  # Extracted from TenantController as static methods
```

**Changes to existing**:
- `TenantController.cs` — extracted into `MultiTenancyPlugin.MapEndpoints()`
- `DynamicPlatformTenantService.cs` — registered by `MultiTenancyPlugin.RegisterServices()`
- `TenantContextMiddleware.cs` — registered by `MultiTenancyPlugin.UseMiddleware()`
- `MetaModelCache.cs` — accept platform entities from plugins
- `Program.cs` — call `MapEndpoints()` + `RegisterServices()` for enabled plugins

**Key design**: `MultiTenancyPlugin` replaces `TenantIsolationFeature` + absorbs
`TenantController` + `DynamicPlatformTenantService` + `TenantContextMiddleware`.
All tenant-related code lives in ONE plugin.

**Tests**: ~40 (entity registration, endpoint mapping, service resolution)

---

### Phase 7: Settings + Menu + Pages
**Goal**: Plugins declare settings schema, menu items, and admin UI page definitions.
Backend serves aggregated manifest. Frontend renders dynamically.

**New files**:
```
src/BMMDL.Runtime/Plugins/
  ISettingsProvider.cs                # Interface + PluginSettingsSchema records
  IMenuContributor.cs                 # Interface + PluginMenuItem record
  IAdminPageProvider.cs               # Interface + PluginPageDefinition record
  PluginManifestService.cs            # Aggregates all providers → manifest JSON

src/BMMDL.Runtime.Api/Controllers/
  PluginController.cs                 # Add: GET /api/plugins/manifest
                                      # Add: PUT /api/plugins/{name}/settings

src/BMMDL.Runtime/Services/
  PluginSettingsService.cs            # Read/write core.plugin_state.settings_json

frontend/src/
  plugins/
    pluginRegistry.ts                 # Fetch + cache manifest
    pluginRouter.ts                   # Generate routes from manifest
    pluginMenu.ts                     # Merge menu items
    components/
      index.ts                        # Component registry
      PluginSettingsForm.vue          # Generic settings form (schema-driven)

  views/admin/
    PluginManagementView.vue          # Plugin install/enable/disable UI
```

**Changes to existing**:
- `router/index.ts` — merge plugin routes at startup
- `AppSidebar.vue` — merge plugin menu items
- `adminService.ts` — add plugin manifest fetching

**Tests**: ~25 backend + manual frontend verification

---

### Phase 8: Middleware Provider + Middleware Ordering
**Goal**: Plugins can register middleware with proper ordering.

**New files**:
```
src/BMMDL.Runtime/Plugins/
  IMiddlewareProvider.cs              # Interface

src/BMMDL.Runtime.Api/
  PluginMiddlewarePipeline.cs         # Orders + registers plugin middleware
```

**Changes to existing**:
- `Program.cs` — use `PluginMiddlewarePipeline` instead of hardcoded middleware

**Tests**: ~15 (ordering, conditional registration)

---

### Phase 9: Refactor Existing Features into Full-Stack Plugins
**Goal**: Convert remaining hardcoded features into full-stack plugins.

| Plugin | What moves in | Priority |
|--------|--------------|----------|
| **MultiTenancy** | TenantController, TenantService, TenantMiddleware, TenantIsolationFeature | Phase 6 (already) |
| **AuditLog** | Audit log viewer (currently hardcoded), audit write hooks | HIGH |
| **UserManagement** | UserController, RoleController, PlatformUserService | HIGH |
| **SoftDelete** | SoftDeleteFeature + new trash bin UI | MEDIUM |
| **Localization** | LocalizationFeature + language manager UI | MEDIUM |
| **FileStorage** | FileReferenceFeature + storage browser UI | MEDIUM |
| **Temporal** | TemporalFeature + history viewer UI | LOW |
| **Sequence** | SequenceFeature + sequence admin UI (exists) | LOW |

Each conversion follows the same pattern:
1. Create XxxPlugin class implementing all relevant interfaces
2. Move existing service/controller code into plugin
3. Add menu items + pages
4. Add settings schema
5. Add migrations (for plugin-owned tables)
6. Verify all existing tests pass
7. Remove old hardcoded registration

**Tests**: existing tests adapted per plugin, ~20 new per plugin

---

### Phase 10: Plugin Admin UI
**Goal**: Full admin experience for managing plugins.

**Frontend pages**:
- `PluginManagementView.vue` — list all plugins with status badges, enable/disable toggles
- `PluginDetailView.vue` — settings form, migration history, dependencies graph
- `PluginSettingsForm.vue` — generic schema-driven settings form (already from Phase 7)

**Features**:
- Install/enable/disable with confirmation dialogs
- Dependency warnings ("Disabling X will also disable Y")
- Settings editor per plugin
- Migration history viewer
- Plugin health status

---

## 10. File Structure (Final State)

```
src/BMMDL.Runtime/Plugins/
  ── Core (existing, extended) ──
  IPlatformFeature.cs
  PlatformFeatureRegistry.cs          # Extended with new typed lists
  FeatureFilterState.cs
  PlatformFeatureExtensions.cs        # Extended DI registration

  ── New Interfaces ──
  IPlatformEntityProvider.cs
  IAdminApiProvider.cs
  IMiddlewareProvider.cs
  IAdminPageProvider.cs
  IMenuContributor.cs
  ISettingsProvider.cs
  IMigrationProvider.cs
  IPluginLifecycle.cs

  ── New Infrastructure ──
  PluginManager.cs                    # State machine: install→enable→disable→uninstall
  PluginManifestService.cs            # Aggregates manifest for frontend
  PluginSettingsService.cs            # Settings CRUD
  PluginState.cs                      # State enum + record types
  PluginMiddlewarePipeline.cs         # Ordered middleware registration

  ── Contexts (existing) ──
  Contexts/
    QueryFilterContext.cs
    InsertContext.cs
    UpdateContext.cs
    DeleteContext.cs
    WriteContext.cs
    PluginContext.cs                   # NEW

  ── Record Types ──
  PluginPageDefinition.cs             # NEW
  PluginMenuItem.cs                   # NEW
  PluginSettingsSchema.cs             # NEW
  PluginMigration.cs                  # NEW

  ── Full-Stack Plugins ──
  Features/
    MultiTenancyPlugin.cs             # Replaces TenantIsolationFeature
    AuditLogPlugin.cs
    UserManagementPlugin.cs
    SoftDeletePlugin.cs               # Extended from SoftDeleteFeature
    LocalizationPlugin.cs             # Extended from LocalizationFeature
    FileStoragePlugin.cs              # Extended from FileReferenceFeature
    TemporalPlugin.cs                 # Extended from TemporalFeature
    AuditFieldPlugin.cs              # Extended from AuditFieldFeature
    SequencePlugin.cs                 # Extended from SequenceFeature
    HasStreamPlugin.cs                # Extended from HasStreamFeature
    InheritancePlugin.cs              # Extended from InheritanceFeature
    ETagPlugin.cs                     # Extended from ETagFeature

src/BMMDL.Runtime.Api/
  Controllers/
    PluginController.cs               # NEW: plugin management API
  Endpoints/
    TenantEndpoints.cs                # NEW: extracted from TenantController
    AuditEndpoints.cs                 # NEW
    TrashEndpoints.cs                 # NEW
  Middleware/
    PluginMiddlewarePipeline.cs       # NEW

frontend/src/
  plugins/
    pluginRegistry.ts
    pluginRouter.ts
    pluginMenu.ts
    components/
      index.ts
      PluginTenantList.vue
      PluginTenantCreate.vue
      PluginAuditLogViewer.vue
      PluginTrashBin.vue
      PluginLanguageManager.vue
      PluginStorageBrowser.vue
      PluginSettingsForm.vue
  views/admin/
    PluginManagementView.vue
    PluginDetailView.vue
```

---

## 11. Migration Strategy

### 11.1 Strangler Fig (Same as Phase 1–4)

Each existing feature is converted incrementally:
1. Create new XxxPlugin that produces IDENTICAL output
2. Run both old and new paths in tests, assert identical results
3. Switch to new path
4. Remove old hardcoded code

### 11.2 Frontend Migration

Phase 1: Plugin manifest drives sidebar + routes (new pages only)
Phase 2: Migrate existing admin pages into plugin components
Phase 3: Remove hardcoded admin routes

### 11.3 Backward Compatibility

- All existing DDL/DML output MUST be byte-identical
- All existing API responses MUST be unchanged
- All existing frontend routes MUST continue working
- Plugins default to "enabled" — no behavior change for existing deployments

---

## 12. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Startup performance (DB read for plugin state) | Slow cold start | Cache plugin state, lazy-load on first access |
| Circular dependencies between plugins | Startup failure | Topological sort already handles (existing) |
| Frontend bundle size (all plugin components) | Slow page load | Lazy-load via dynamic import (already planned) |
| Plugin disable breaks dependent data | Data loss | Soft-disable: hide UI, keep data accessible via API |
| Migration rollback fails | Corrupted schema | DownSql required + test in CI |
| Settings change breaks running requests | Runtime errors | Settings reload on next request, not mid-request |

---

## 13. Success Criteria

- [ ] `MultiTenancyPlugin` fully replaces TenantIsolationFeature + TenantController + TenantService + TenantMiddleware
- [ ] Disabling MultiTenancy plugin removes: tenant API, tenant UI, tenant menu, tenant middleware, tenant_id columns
- [ ] Enabling MultiTenancy plugin restores all of the above
- [ ] A new plugin (e.g., AuditLog) can be created with ZERO changes to existing code
- [ ] Plugin settings are editable via admin UI
- [ ] All 2579+ existing tests pass with zero regressions
- [ ] Frontend sidebar + routes update dynamically based on enabled plugins

---

## 14. Phase Summary

| Phase | Scope | Est. Files | Est. Tests | Depends On |
|-------|-------|-----------|-----------|------------|
| **5** | Plugin Manager + State DB | ~8 | ~30 | Phase 4 (done) |
| **6** | Entity Provider + API Provider + MultiTenancy refactor | ~12 | ~40 | Phase 5 |
| **7** | Settings + Menu + Pages + Frontend plugin system | ~15 | ~25 | Phase 6 |
| **8** | Middleware Provider + ordering | ~4 | ~15 | Phase 6 |
| **9** | Refactor remaining features into full-stack plugins | ~20 | ~100 | Phase 7, 8 |
| **10** | Plugin Admin UI (management + detail + settings) | ~6 | ~10 | Phase 7 |

**Recommended order**: 5 → 6 → 7 → 8 → 9 → 10

Phases 7 and 8 can run in parallel after Phase 6.
Phase 10 can start after Phase 7.
