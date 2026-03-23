# CLAUDE.md - BMMDL Project Guide

## General Rules

- **Never run a command twice to get more detail.** Always run commands once with sufficient verbosity (e.g., `--verbosity normal` for dotnet). Extract both summary and error details from that single output. This applies to tests, builds, compilations, curl calls — everything.

## Project Overview

**BMMDL (Business Meta Model Definition Language)** is an enterprise-grade Domain-Specific Language platform for defining business data models, similar to SAP CDS. It provides a complete solution for:

- **DSL Compilation**: ANTLR4-based multi-pass compiler
- **Code Generation**: PostgreSQL DDL and migration scripts
- **Runtime Execution**: Dynamic CRUD, OData queries, business rules
- **Multi-tenancy**: Built-in tenant isolation
- **Temporal Data**: Bitemporal support (transaction + valid time)

## Quick Start Commands

```bash
# Build the solution
dotnet build BMMDL.sln

# Run unit tests
dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj

# Run E2E tests (requires PostgreSQL)
dotnet test src/BMMDL.Tests.New/BMMDL.Tests.New.csproj

# Run Registry API (port 51742)
dotnet run --project src/BMMDL.Registry.Api/BMMDL.Registry.Api.csproj

# Run Runtime API (port 5175)
dotnet run --project src/BMMDL.Runtime.Api/BMMDL.Runtime.Api.csproj

# Run Frontend (port 5173)
cd frontend && npm install && npm run dev
```

## Project Structure

```
BMMDL/
├── Grammar/                    # ANTLR4 grammar files
│   ├── BmmdlLexer.g4          # Lexer rules (tokens, keywords)
│   └── BmmdlParser.g4         # Parser rules (grammar productions)
├── samples/                    # Sample BMMDL files
├── erp_modules/                # Domain modules (common.bmmdl + 12 modules)
├── frontend/                   # Vue 3 + TypeScript + Tailwind CSS frontend
│   └── src/
│       ├── components/entity/  # Entity CRUD components (forms, tables, compositions)
│       ├── composables/        # Vue composables (useAuth, useOData, useMetadata, useTenant)
│       ├── services/           # API clients (adminService, odataService, metadataService)
│       ├── utils/              # Helpers (associationDisplay, formValidator, odataQueryBuilder)
│       └── views/              # Pages (entity CRUD, admin modules, auth, dashboard)
├── src/
│   ├── BMMDL.MetaModel/       # Core domain model (no dependencies)
│   ├── BMMDL.Compiler/        # 12-pass compilation pipeline
│   ├── BMMDL.CodeGen/         # PostgreSQL DDL generation
│   ├── BMMDL.Registry/        # Meta-model persistence (EF Core)
│   ├── BMMDL.SchemaManager/   # Database schema operations
│   ├── BMMDL.Runtime/         # Execution engine, CRUD, rules, plugin system
│   │   └── Plugins/           # Plugin architecture (Features/, Loading/, interfaces)
│   ├── BMMDL.Registry.Api/    # REST API for registry + admin module management
│   ├── BMMDL.Runtime.Api/     # REST API for runtime (OData)
│   ├── BMMDL.Tests/           # Unit tests
│   ├── BMMDL.Tests.New/       # E2E tests (current)
│   └── BMMDL.Tests.E2E/       # E2E tests (legacy, deprecated)
└── BMMDL.sln
```

## Key Files by Task

### DSL Grammar Changes
- `Grammar/BmmdlLexer.g4` - Add tokens/keywords
- `Grammar/BmmdlParser.g4` - Add grammar rules
- `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs` - AST construction
- `src/BMMDL.Compiler/Parsing/BmExpressionBuilder.cs` - Expression parsing

### Compiler Pipeline
- `src/BMMDL.Compiler/Pipeline/CompilerPipeline.cs` - Pipeline orchestration
- `src/BMMDL.Compiler/Pipeline/Passes/` - Individual compilation passes
- `src/BMMDL.Compiler/Validation/` - Semantic validators

### Code Generation
- `src/BMMDL.CodeGen/PostgresDdlGenerator.cs` - DDL generation
- `src/BMMDL.CodeGen/Schema/MigrationScriptGenerator.cs` - Migrations
- `src/BMMDL.CodeGen/Visitors/PostgresSqlExpressionVisitor.cs` - SQL expressions

### Runtime/API
- `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` - Query building (incl. `$expand` with JOIN)
- `src/BMMDL.Runtime/DataAccess/ParameterizedQueryExecutor.cs` - Query execution
- `src/BMMDL.Runtime/Rules/RuleEngine.cs` - Business rules
- `src/BMMDL.Runtime/Authorization/PermissionChecker.cs` - Access control evaluation
- `src/BMMDL.Runtime/Expressions/RuntimeExpressionEvaluator.cs` - Expression evaluation
- `src/BMMDL.Runtime/MetaModelCache.cs` - In-memory model cache
- `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` - OData CRUD endpoints
- `src/BMMDL.Runtime.Api/Controllers/ODataServiceController.cs` - Service actions
- `src/BMMDL.Runtime.Api/Controllers/ODataMetadataController.cs` - `$metadata` and service document
- `src/BMMDL.Runtime.Api/Controllers/BatchController.cs` - Batch operations
- `src/BMMDL.Runtime.Api/Controllers/EntityNavigationController.cs` - Navigation property access
- `src/BMMDL.Runtime.Api/Controllers/EntityReferenceController.cs` - `$ref` relationship management
- `src/BMMDL.Runtime.Api/Controllers/EntityActionController.cs` - Bound actions/functions
- `src/BMMDL.Runtime.Api/Handlers/DeepInsertHandler.cs` - Nested entity creation
- `src/BMMDL.Runtime.Api/Handlers/DeepUpdateHandler.cs` - Nested entity modification
- `src/BMMDL.Runtime.Api/Handlers/RecursiveExpandHandler.cs` - `$levels` recursive expand

### Plugin System
- `src/BMMDL.Runtime/Plugins/` - Plugin core: interfaces, registry, manager, loading
- `src/BMMDL.Runtime/Plugins/Features/` - Built-in plugins (12 DDL/DML + 2 full-stack + 6 infrastructure)
- `src/BMMDL.Runtime/Plugins/PlatformFeatureRegistry.cs` - Topological sort, capability caching
- `src/BMMDL.Runtime/Plugins/PluginManager.cs` - Lifecycle management (install/enable/disable/uninstall), bootstrap
- `src/BMMDL.Runtime/Plugins/PlatformFeatureExtensions.cs` - DI registration helpers
- `src/BMMDL.Runtime/Plugins/Loading/PluginDirectoryLoader.cs` - External DLL plugin loading
- `src/BMMDL.Runtime/Plugins/PluginManifestService.cs` - Frontend manifest aggregation

### Registry/Admin
- `src/BMMDL.Registry.Api/Services/AdminService.cs` - Module compile & install
- `src/BMMDL.Registry.Api/Controllers/AdminController.cs` - Admin API endpoints
- `src/BMMDL.Registry.Api/Models/AdminModels.cs` - Admin request/response models

### Meta-Model
- `src/BMMDL.MetaModel/BmModel.cs` - Root model
- `src/BMMDL.MetaModel/Structure/` - Entity, Field, Association
- `src/BMMDL.MetaModel/Expressions/` - Expression AST

### Frontend (Vue 3 + TypeScript)
- `frontend/src/views/entity/EntityListView.vue` - Entity list with filtering/pagination
- `frontend/src/views/entity/EntityDetailView.vue` - Entity detail with composition sections
- `frontend/src/views/entity/EntityCreateView.vue` - Entity creation with deep insert
- `frontend/src/views/entity/EntityEditView.vue` - Entity editing with deep update
- `frontend/src/views/admin/AdminModulesView.vue` - Module management (compile/install)
- `frontend/src/components/entity/CompositionSection.vue` - Inline composition display
- `frontend/src/components/entity/CompositionFormRows.vue` - Inline composition editing
- `frontend/src/components/entity/fields/` - Type-specific field components (Association, Enum, etc.)
- `frontend/src/services/odataService.ts` - OData API client
- `frontend/src/services/metadataService.ts` - Metadata/schema fetching
- `frontend/src/services/adminService.ts` - Admin module management client
- `frontend/src/utils/associationDisplay.ts` - FK display name resolution

## Coding Conventions

### Naming
- **C# Classes**: PascalCase (e.g., `BmEntity`, `DynamicSqlBuilder`)
- **Database Columns**: snake_case (e.g., `tenant_id`, `created_at`)
- **DSL Keywords**: lowercase (e.g., `entity`, `service`, `rule`)
- **Namespaces**: Match folder structure

### Database
- Primary database: PostgreSQL
- Schema `registry` for meta-model storage
- Schema `platform` for runtime data
- Use parameterized queries (Npgsql)

### Testing
- Unit tests: xUnit + FluentAssertions
- Database tests: Testcontainers.PostgreSql
- Test naming: `MethodName_Scenario_ExpectedResult`

## DSL Syntax Quick Reference

```bmmdl
// Module definition (new structured syntax)
module MyApp version '1.0' {
    author "BMMDL Team";
    description "Sample application module";
    depends core version '1.0';

    namespace business.crm {
        // Entities, types, services defined here
    }
}

// Entity definition
entity Customer : Auditable {
    key ID: UUID;
    name: String(100);
    email: String(255);
    status: CustomerStatus default #Active;

    // Relationships
    orders: composition [*] of Order;
    account: association [0,1] to Account;

    // Computed field
    virtual orderCount: Integer computed = count(orders);
}

// Enum
enum CustomerStatus {
    Active = 1;
    Inactive = 2;
    Suspended = 3;
}

// Business rule
rule ValidateCustomer for Customer on before create {
    validate email like '%@%.%'
        message 'Invalid email format'
        severity error;
}

// Access control
access control for Customer {
    grant read to authenticated;
    grant create, update to role 'Sales';
    grant delete to role 'Admin';
}

// Service
service CustomerService {
    entity Customers as Customer;
    action activate(customerId: UUID) returns Customer;
}

// File storage field
entity Document {
    key ID: UUID;
    name: String(255);

    @Storage.Provider: 'S3'
    @Storage.Bucket: 'documents'
    @Storage.MaxSize: 10485760
    @Storage.AllowedTypes: ['application/pdf', 'image/*']
    attachment: FileReference;
    // Expands to: attachment_provider, attachment_bucket, attachment_key,
    //             attachment_size, attachment_mime_type, attachment_checksum,
    //             attachment_uploaded_at, attachment_uploaded_by
}

// Temporal entity (bitemporal support)
@Temporal(strategy: 'InlineHistory')
@Temporal.ValidTime(from: 'effectiveFrom', to: 'effectiveTo')
entity PriceHistory {
    key ID: UUID;
    productId: UUID;
    price: Decimal(18,2);
    effectiveFrom: Date;
    effectiveTo: Date;
    // Auto-generated: system_start, system_end, version
}
```

## Type System

### Primitive Types
| Type | PostgreSQL | Notes |
|------|------------|-------|
| `String(n)` | `varchar(n)` | Length required |
| `Integer` | `integer` | 32-bit signed |
| `Decimal(p,s)` | `numeric(p,s)` | Precision, scale |
| `Boolean` | `boolean` | true/false |
| `Date` | `date` | Date only |
| `Time` | `time` | Time only |
| `DateTime` | `timestamp` | Date + time |
| `Timestamp` | `timestamptz` | With timezone |
| `UUID` | `uuid` | Primary keys |
| `Binary` | `bytea` | Binary data |

### Special Types
- `localized String(n)` - Multi-language text (generates _texts table)
- `Array<Type>` - PostgreSQL arrays
- `FileReference` - External file storage (expands to 8 metadata columns)
- `CustomType` - User-defined structured types

### Cardinality Notation
- `[0..1]` - Optional, single (ManyToOne)
- `[1..1]` - Required, single (OneToOne)
- `[*]` or `[0..*]` - Optional, multiple (OneToMany)
- `[1..*]` - Required, multiple (OneToMany)

## Architecture Notes

### Compiler Passes (in order)
1. **LexicalPass** - Tokenization
2. **SyntacticPass** - Parse to AST
3. **ModelBuildPass** - Build BmModel
4. **SymbolResolutionPass** - Register/resolve symbols
5. **DependencyGraphPass** - Build dependency graph
6. **ExpressionDependencyPass** - Expression analysis
7. **BindingPass** - Type binding/inference
8. **TenantIsolationPass** - Tenant validation
9. **FileStorageValidationPass** - Storage validation
10. **TemporalValidationPass** - Temporal data validation
11. **SemanticValidationPass** - Semantic rules
12. **OptimizationPass** - Aspect inlining, deduplication

### Plugin Architecture
- **20 built-in plugins**: 10 DDL/DML features + 2 full-stack (MultiTenancy, PlatformIdentity) + 6 infrastructure + 2 API companions
- **Plugin interfaces**: `IPlatformFeature` (base), `IPluginLifecycle`, `IBmmdlModuleProvider`, `IPlatformEntityProvider`, `IAdminPageProvider`, `IMenuContributor`, `ISettingsProvider`, `IMigrationProvider`, `IGlobalFeature`
- **DDL/DML features**: AuditField, SoftDelete, Inheritance, Localization, Temporal, Sequence, FileReference, HasStream, FieldEncryption, ChangeDataCapture
- **Full-stack plugins**: MultiTenancyPlugin (Tenant entity + tenant isolation), PlatformIdentityPlugin (Identity/User/Role/UserRole + default roles + permissions)
- **Infrastructure plugins**: AuditLogging, EventOutbox, Webhooks, Collaboration, Reporting, UserPreferences
- **Discovery**: `FeatureDiscovery.DiscoverBuiltInFeatures()` auto-discovers from assembly
- **Registry**: `PlatformFeatureRegistry` — topological sort by `Stage` + `DependsOn`, volatile snapshot for thread safety
- **Bootstrap**: `PluginManager.BootstrapBuiltInPluginsAsync()` auto-installs/enables at startup
- **External plugins**: `PluginDirectoryLoader` loads DLLs from `plugins/` directory via isolated `AssemblyLoadContext`
- **Plugin manifest**: `plugin.json` per plugin with name/version/entryAssembly/dependencies/bmmdlModules
- **Module compilation**: `InstallPluginModulesAsync` → `IBmmdlModuleProvider.GetModules()` → Registry API compile + schema init

### Multi-Tenancy
- Tenant ID from JWT claims
- Automatic WHERE filtering for tenant-scoped entities
- Connection factory per tenant
- `MultiTenancyPlugin` provides: Tenant entity, DDL (tenant_id column + RLS), DML filters, admin pages, settings, System Tenant seeding

### Temporal Data
- **InlineHistory**: All versions in same table (system_start, system_end)
- **SeparateTables**: Main table + history table with triggers
- Query params: `asOf`, `validAt`, `includeHistory`

### OData v4 Support
- **Query Options**: `$filter`, `$select`, `$expand`, `$orderby`, `$top`, `$skip`, `$count`, `$search`, `$apply`, `$compute`
- **Expansion**: `$expand` with LEFT JOIN (ManyToOne) and batch sub-queries (OneToMany); `$levels` for recursive hierarchies
- **Deep Insert/Update**: Nested entity creation/modification via compositions in single request
- **ETag**: Optimistic concurrency via weak ETags and `If-Match` header (412 on mismatch)
- **Metadata**: Full `$metadata` CSDL with EntityTypes, NavigationProperties, ReferentialConstraints, ContainsTarget, Actions, Functions
- **Batch**: `$batch` endpoint (JSON format) with dependency tracking
- **$ref**: Relationship management (POST/PUT/DELETE) via `EntityReferenceController`
- **Bound Operations**: Entity-bound actions/functions via `EntityActionController`
- **Unbound Operations**: Service-level actions/functions via `ODataServiceController`
- **Containment**: Nested resource CRUD for compositions via `EntityActionController`
- **Async**: 202 Accepted pattern with operation monitoring via `AsyncOperationController`
- **$value**: Property-level raw value access (GET/PUT/DELETE)
- **Delta Responses**: `$deltatoken` and `@odata.deltaLink` for change tracking via `DeltaTokenService`
- **Singleton Entities**: `@OData.Singleton` annotation, keyless GET/PATCH routing
- **Capability Annotations**: OData vocabulary annotations (Filter, Sort, Expand, Insert, Update, Delete restrictions) in CSDL
- **Prefer Header**: `return=minimal` (204), `return=representation`, `odata.maxpagesize` with `Preference-Applied`
- **Computed/ReadOnly Fields**: `Org.OData.Core.V1.Computed` and `Immutable` annotations in CSDL; computed fields stripped from input

### Runtime Architecture
- **MetaModelCache**: In-memory O(1) lookups for entities, rules, access controls. Falls back to `IPlatformEntityProvider` when registry DB is empty
- **MetaModelCacheManager**: Cache lifecycle management with `PlatformFeatureRegistry` fallback
- **DynamicSqlBuilder**: Parameterized SQL generation (never string interpolation); `$expand` via LEFT JOIN (ManyToOne) and batch sub-queries (OneToMany)
- **DeepInsertHandler / DeepUpdateHandler**: Composition-aware nested CRUD with auto FK population
- **RecursiveExpandHandler**: `$levels` recursive expansion with depth limiting (max 10)
- **BatchController**: Multi-operation `$batch` with dependency tracking
- **EntityReferenceController**: `$ref` relationship management (POST/PUT/DELETE)
- **EntityActionController**: Bound actions/functions + containment CRUD
- **RuleEngine**: Before/After triggers for CRUD operations
- **PermissionChecker**: Fail-close policy (no rules = DENY)
- **RuntimeExpressionEvaluator**: Evaluates compiled expression ASTs
- **PluginManager**: Plugin lifecycle (install/enable/disable/uninstall), BMMDL module compilation, migration management, bootstrap

## Common Tasks

### Get Help
1. Always use Context7 MCP when I need .NET 10 library/API documentation, setup or configuration steps without me having to explicitly ask.

### Adding a New Plugin
1. Create plugin class in `src/BMMDL.Runtime/Plugins/Features/` implementing `IPlatformFeature` + relevant interfaces
2. Set `Name`, `Stage` (execution order), `DependsOn` (dependencies)
3. Implement `IPluginLifecycle` for table creation in `OnInstalledAsync` (use `ITenantConnectionFactory` from `PluginContext.Services`)
4. Implement `ISettingsProvider` for configuration schema
5. Implement `IAdminPageProvider` + `IMenuContributor` for admin UI
6. Optionally implement `IBmmdlModuleProvider` for BMMDL-defined entities
7. Plugin is auto-discovered via `FeatureDiscovery.DiscoverBuiltInFeatures()`
8. Add tests following `MultiTenancyPluginTests.cs` pattern

### Adding a New DSL Feature
1. Add tokens to `BmmdlLexer.g4`
2. Add grammar rules to `BmmdlParser.g4`
3. Add model classes to `BMMDL.MetaModel`
4. Update `BmmdlModelBuilder.cs` to build AST
5. Add validation in appropriate pass
6. Update code generation if needed
7. Add tests

### Adding an API Endpoint
1. Add controller/action in `BMMDL.Runtime.Api`
2. Add authorization checks
3. Use `DynamicSqlBuilder` for queries
4. Follow existing patterns in `DynamicEntityController`

### Debugging Compilation
- Use `RichDiagnosticFormatter` for error display
- Check `CompilationResult.Diagnostics`
- Enable verbose logging in pipeline

### CLI Compiler Commands (bmmdlc)
```bash
# Validate BMMDL files
dotnet run --project src/BMMDL.Compiler -- validate samples/*.bmmdl

# Compile with verbose output
dotnet run --project src/BMMDL.Compiler -- compile samples/*.bmmdl --verbose

# Initialize database schema
dotnet run --project src/BMMDL.Compiler -- init-schema

# Apply migrations
dotnet run --project src/BMMDL.Compiler -- migrate-schema

# Bootstrap (full init + compile + migrate)
dotnet run --project src/BMMDL.Compiler -- bootstrap samples/*.bmmdl
```

## Environment Variables

```bash
# Database connections
REGISTRY_CONNECTION_STRING=Host=localhost;Database=bmmdl_registry;...
PLATFORM_CONNECTION_STRING=Host=localhost;Database=bmmdl_platform;...

# JWT settings
JWT_SECRET=your-secret-key
JWT_ISSUER=bmmdl
JWT_AUDIENCE=bmmdl-api

# Admin API key (header: X-Admin-Key)
ADMIN_API_KEY=bmmdl-admin-key-change-me

# Logging
ASPNETCORE_ENVIRONMENT=Development
```

## Troubleshooting

### Common Issues

**ANTLR errors**: Regenerate parser with `dotnet build` (automatic via MSBuild)

**Migration failures**: Check `__bmmdl_migrations` table for applied migrations

**Tenant isolation errors**: Ensure entity has `@TenantScoped` annotation

**Temporal query issues**: Verify `system_start`/`system_end` columns exist

## Dependencies

### Backend
- .NET 10.0
- PostgreSQL 15+
- ANTLR4 4.13.1
- Entity Framework Core 10.0
- Npgsql 10.0
- xUnit 2.6.4

### Frontend
- Vue 3 + TypeScript
- Vite
- Tailwind CSS
- Vue Router

## Error Codes Reference

| Category | Prefix | Example |
|----------|--------|---------|
| Lexical/Parsing | LEX, SYN | LEX001 (file error), SYN002 (parse error) |
| Model Building | MOD | MOD001 (build error) |
| Symbol Resolution | SYM | SYM_UNRESOLVED_REF (unresolved reference) |
| Dependency | DEP | DEP_CIRCULAR_ENTITY, DEP_CIRCULAR_EXPRESSION |
| Tenant Isolation | TENANT | TENANT_MISSING_ID, TENANT_GLOBAL_REFS_SCOPED |
| Temporal | TEMP | TEMP001-TEMP012 (various temporal rules) |
| File Storage | FILE | FILE001-FILE007 (storage validation) |
| Semantic | SEM | SEM_ENTITY_NO_KEY, SEM_DUPLICATE_FIELD |

Error codes defined in: `src/BMMDL.Compiler/ErrorCodes.cs`

## Resources

- Sample BMMDL files: `samples/` directory
- Grammar reference: `Grammar/` directory
- Test examples: `src/BMMDL.Tests/`
- Architecture docs: `docs/` directory
- ERP modules: `erp_modules/` directory (12 domain modules)
