# BMMDL Architectural Design Document

## 1. Executive Summary

BMMDL (Business Meta Model Definition Language) is a comprehensive Domain-Specific Language (DSL) platform designed for enterprise business data modeling. Comparable to SAP CDS or Microsoft Entity Framework conceptually, BMMDL provides a complete solution from model definition through runtime execution.

### Core Capabilities
- **DSL Definition**: Rich grammar for entities, services, rules, and access control
- **Multi-Pass Compilation**: ANTLR4-based compiler with 12 semantic passes
- **Code Generation**: PostgreSQL DDL, migrations, and type mappings
- **Runtime Execution**: Dynamic CRUD, OData queries, business rules engine
- **Multi-Tenancy**: Built-in tenant isolation at all layers
- **Temporal Data**: Bitemporal support (transaction time + valid time)
- **Module System**: Versioned modules with dependency management

---

## 2. System Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CLIENT LAYER                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │ Web Apps    │  │ Mobile Apps │  │ CLI Tools   │  │ External Services   │ │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘ │
└─────────┼────────────────┼────────────────┼────────────────────┼────────────┘
          │                │                │                    │
          └────────────────┼────────────────┼────────────────────┘
                           ▼                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                               API LAYER                                      │
│  ┌──────────────────────────────┐  ┌──────────────────────────────────────┐ │
│  │      Registry API            │  │           Runtime API                │ │
│  │  ┌────────────────────────┐  │  │  ┌────────────────────────────────┐ │ │
│  │  │ Module Management      │  │  │  │ Dynamic Entity Controller      │ │ │
│  │  │ Version Control        │  │  │  │ (OData: $filter, $expand,      │ │ │
│  │  │ Migration Workflow     │  │  │  │  $apply, $search, $orderby)    │ │ │
│  │  │ Upgrade Orchestration  │  │  │  ├────────────────────────────────┤ │ │
│  │  └────────────────────────┘  │  │  │ Service Controller             │ │ │
│  └──────────────────────────────┘  │  │ (Functions & Actions)          │ │ │
│                                     │  ├────────────────────────────────┤ │ │
│                                     │  │ Auth Controller (JWT/OAuth)    │ │ │
│                                     │  └────────────────────────────────┘ │ │
│                                     └──────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                           │                │
                           ▼                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            RUNTIME LAYER                                     │
│  ┌────────────────────┐  ┌────────────────────┐  ┌────────────────────────┐ │
│  │ MetaModelCache     │  │ DynamicRepository   │  │ RuleEngine             │ │
│  │ (In-memory index)  │  │ (CRUD Operations)   │  │ (Validate/Compute/When)│ │
│  └────────────────────┘  └────────────────────┘  └────────────────────────┘ │
│  ┌────────────────────┐  ┌────────────────────┐  ┌────────────────────────┐ │
│  │ DynamicSqlBuilder  │  │ OData Parsers      │  │ EventPublisher         │ │
│  │ (Query Generation) │  │ (Filter/Expand/    │  │ (Domain Events)        │ │
│  │                    │  │  Apply/Search)     │  │                        │ │
│  └────────────────────┘  └────────────────────┘  └────────────────────────┘ │
│  ┌────────────────────┐  ┌────────────────────┐  ┌────────────────────────┐ │
│  │ PermissionChecker  │  │ FieldRestriction   │  │ SequenceService        │ │
│  │ (Access Control)   │  │ Applier (Masking)  │  │ (ID Generation)        │ │
│  └────────────────────┘  └────────────────────┘  └────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                           │                │
                           ▼                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           COMPILER LAYER                                     │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                      CompilerPipeline (12 Passes)                     │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────────────┐ │   │
│  │  │Lexical  │→│Syntactic│→│Model    │→│Symbol   │→│Dependency Graph │ │   │
│  │  │Pass     │ │Pass     │ │Build    │ │Resolve  │ │Pass             │ │   │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────────────┘ │   │
│  │       ↓           ↓           ↓           ↓              ↓           │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────────────┐ │   │
│  │  │Binding  │→│Tenant   │→│Temporal │→│Semantic │→│Optimization     │ │   │
│  │  │Pass     │ │Isolation│ │Validate │ │Validate │ │Pass             │ │   │
│  │  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────────────┘ │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                    ↓                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                         Code Generation                               │   │
│  │  ┌────────────────┐  ┌──────────────────┐  ┌───────────────────────┐ │   │
│  │  │PostgresDdl     │  │MigrationScript   │  │ExpressionTranslator   │ │   │
│  │  │Generator       │  │Generator         │  │(BMMDL→SQL)            │ │   │
│  │  └────────────────┘  └──────────────────┘  └───────────────────────┘ │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                           │                │
                           ▼                ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          DATA LAYER                                          │
│  ┌─────────────────────────────┐  ┌─────────────────────────────────────┐   │
│  │   Registry Database         │  │        Platform Database            │   │
│  │   (Schema: registry)        │  │        (Schema: platform)           │   │
│  │  ┌────────────────────────┐ │  │  ┌────────────────────────────────┐ │   │
│  │  │ Modules, Versions      │ │  │  │ Tenant Data (dynamic tables)  │ │   │
│  │  │ Tenants, Installations │ │  │  │ History Tables (temporal)     │ │   │
│  │  │ Normalized Meta-Model  │ │  │  │ Audit Logs                    │ │   │
│  │  │ (100+ tables)          │ │  │  │                               │ │   │
│  │  └────────────────────────┘ │  │  └────────────────────────────────┘ │   │
│  └─────────────────────────────┘  └─────────────────────────────────────┘   │
│                               PostgreSQL                                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Layer Descriptions

### 3.1 MetaModel Layer (Foundation)

**Project**: `BMMDL.MetaModel`

The MetaModel layer defines the core domain model representing all DSL constructs. It has zero external dependencies, serving as the foundation for all other layers.

#### Key Components

| Component | Purpose |
|-----------|---------|
| `BmModel` | Root aggregate containing all DSL constructs |
| `BmEntity` | Entity definition with fields, relationships, temporal config |
| `BmField` | Field with type, nullability, computed expressions |
| `BmAssociation` | ManyToOne/OneToOne relationships |
| `BmComposition` | Parent-child hierarchical relationships |
| `BmService` | Service boundary with exposed entities and operations |
| `BmRule` | Business rule with triggers and statements |
| `BmAccessControl` | Access control policies per entity |
| `BmSequence` | ID sequence generator with patterns |
| `BmEvent` | Domain event definitions |

#### Expression AST Hierarchy

```
BmExpression (abstract)
├── BmLiteralExpression (String, Integer, Decimal, Boolean, Null, Enum)
├── BmIdentifierExpression (field paths: customer.address.city)
├── BmContextVariableExpression ($now, $userId, $tenantId)
├── BmParameterExpression (:customerId)
├── BmBinaryExpression (14 operators: +, -, *, /, =, <>, AND, OR, etc.)
├── BmUnaryExpression (NOT, negate)
├── BmFunctionCallExpression (UPPER, SUBSTRING, etc.)
├── BmAggregateExpression (COUNT, SUM, AVG, MIN, MAX)
├── BmCaseExpression (CASE WHEN...THEN...ELSE...END)
├── BmTernaryExpression (condition ? then : else)
├── BmCastExpression (CAST AS)
├── BmInExpression (IN/NOT IN)
├── BmBetweenExpression (BETWEEN...AND)
├── BmLikeExpression (LIKE/NOT LIKE)
├── BmIsNullExpression (IS NULL/IS NOT NULL)
└── BmParenExpression (grouping)
```

---

### 3.2 Compiler Layer

**Projects**: `BMMDL.Parser`, `BMMDL.Compiler`

The compiler transforms BMMDL source code into a validated semantic model through a 12-pass pipeline. ANTLR4-generated parser code is in the separate `BMMDL.Parser` project.

#### Grammar Files (ANTLR4)

| File | Lines | Purpose |
|------|-------|---------|
| `BmmdlLexer.g4` | ~495 | Token definitions, keywords, operators |
| `BmmdlParser.g4` | ~750 | Grammar rules, AST structure |

#### Compilation Pipeline

```
Source Files (.bmmdl)
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 1: LEXICAL ANALYSIS                         │
│  • Parallel tokenization of source files                            │
│  • Creates token streams per file                                   │
│  • Collects lexer errors                                            │
└────────────────────────────────────────────────────────────────────┘
         │ Token Streams
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 2: SYNTACTIC ANALYSIS                       │
│  • ANTLR4 parser generates parse trees                              │
│  • Validates grammar compliance                                     │
│  • Collects syntax errors                                           │
└────────────────────────────────────────────────────────────────────┘
         │ Parse Trees
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 3: MODEL BUILDING                           │
│  • Visitor pattern traversal of parse trees                         │
│  • Builds BmModel with expression ASTs                              │
│  • Merges multi-file models                                         │
└────────────────────────────────────────────────────────────────────┘
         │ BmModel (unresolved)
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 4: SYMBOL RESOLUTION                        │
│  • Registers all symbols (entities, types, fields, services)        │
│  • Resolves cross-file references                                   │
│  • Builds symbol table                                              │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 5: DEPENDENCY GRAPH                         │
│  • Builds directed graph of model dependencies                      │
│  • Detects circular dependencies                                    │
│  • Used for dead-code analysis                                      │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 6: EXPRESSION DEPENDENCIES                  │
│  • Analyzes which expressions depend on which fields                │
│  • Tracks computed field dependencies                               │
│  • Used for incremental compilation                                 │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 7: BINDING & TYPE INFERENCE                 │
│  • Binds identifier references to symbols                           │
│  • Infers types for all expressions                                 │
│  • Validates type compatibility                                     │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 8: TENANT ISOLATION                         │
│  • Validates tenant-scoped entity configuration                     │
│  • Checks cross-tenant reference violations                         │
│  • Verifies TenantAware aspect application                          │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 9: FILE STORAGE VALIDATION                  │
│  • Validates file/blob storage configurations                       │
│  • Ensures storage backend compatibility                            │
│  • FileReference field expansion validation                         │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 10: TEMPORAL VALIDATION                     │
│  • Validates @Temporal annotations                                  │
│  • Checks valid-time column references                              │
│  • Prevents reserved column names (system_start, system_end)        │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 11: SEMANTIC VALIDATION                     │
│  • Entity validation (fields, keys, duplicates)                     │
│  • Computed field validation                                        │
│  • Association/rule/access control validation                       │
│  • Type validation                                                  │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Pass 12: OPTIMIZATION                            │
│  • Aspect inlining (merges aspect fields into entities)             │
│  • Type deduplication                                               │
│  • Namespace merging                                                │
└────────────────────────────────────────────────────────────────────┘
         │
         ▼
    CompilationResult
    {
      Success: bool,
      Model: BmModel,
      Diagnostics: []
    }
```

---

### 3.3 Code Generation Layer

**Project**: `BMMDL.CodeGen`

Generates PostgreSQL DDL, migrations, and SQL expressions from compiled models.

#### Key Components

| Component | Responsibility |
|-----------|----------------|
| `PostgresDdlGenerator` | CREATE TABLE, INDEX, CONSTRAINT DDL |
| `TypeResolver` | BMMDL → PostgreSQL type mapping |
| `ExpressionTranslator` | BMMDL expressions → SQL expressions |
| `SchemaDiffer` | Compare schema versions, detect changes |
| `MigrationScriptGenerator` | Generate UP/DOWN migration scripts |
| `MigrationExecutor` | Execute migrations against database |

#### Type Mapping

| BMMDL Type | PostgreSQL Type |
|------------|-----------------|
| `String(n)` | `VARCHAR(n)` |
| `Integer` | `INTEGER` |
| `Decimal(p,s)` | `NUMERIC(p,s)` |
| `UUID` | `UUID` |
| `Boolean` | `BOOLEAN` |
| `Date` | `DATE` |
| `DateTime` | `TIMESTAMP` |
| `Timestamp` | `TIMESTAMPTZ` |

---

### 3.4 Registry Layer

**Project**: `BMMDL.Registry`

Persists meta-model definitions and manages module lifecycle using EF Core.

#### Database Schema (registry)

**Module Lifecycle Tables**:
- `Tenants` - Multi-tenant isolation units
- `Modules` - DSL module definitions with versions
- `ModuleDependencies` - Module dependency graph
- `ModuleInstallations` - Tenant → Module installations
- `Migrations` - Applied migration tracking
- `BreakingChanges` - Incompatibility detection

**Normalized Meta-Model Storage** (100+ tables):
- Entities, Fields, Associations, Indexes, Constraints
- Types, Enums, Aspects
- Services, Operations, Views
- Rules, Triggers, Statements
- Access Controls, Rules, Field Restrictions
- Sequences, Events
- Expression Nodes (self-referencing AST tree)

**Version Management**:
- `ObjectVersions` - Tracks object evolution
- `UpgradeWindows` - Coordination periods
- `UpgradeSyncStatus` - Per-entity upgrade progress

#### Key Services

| Service | Responsibility |
|---------|----------------|
| `VersioningService` | Detect changes, generate migrations |
| `ModuleInstallationService` | Installation workflow orchestration |
| `DependencyResolver` | Module dependency resolution |
| `ChangeDetector` | Diff existing vs. incoming models |
| `DualVersionSyncService` | Sync data during version upgrades |
| `UpgradeJobService` | Upgrade window management |

---

### 3.5 Schema Manager Layer

**Project**: `BMMDL.SchemaManager`

Manages database schema initialization and migrations.

#### Key Operations

| Operation | Description |
|-----------|-------------|
| `InitializeSchemaAsync` | Create all tables from BmModel |
| `MigrateSchemaAsync` | Detect and apply schema changes |
| `RollbackSchemaAsync` | Undo previous migrations |

#### Migration Tracking

Migrations are tracked in `__bmmdl_migrations` table with:
- Migration name
- Applied timestamp
- Checksum for integrity validation

---

### 3.6 Runtime Layer

**Project**: `BMMDL.Runtime`

Executes compiled models at runtime with CRUD, OData, rules, and events.

#### Data Access Components

```
┌─────────────────────────────────────────────────────────────────┐
│                     Request Processing                          │
│  ┌─────────────┐    ┌───────────────┐    ┌─────────────────┐   │
│  │MetaModel    │ →  │DynamicSql     │ →  │Parameterized    │   │
│  │Cache        │    │Builder        │    │QueryExecutor    │   │
│  │(O(1) lookup)│    │(SQL gen)      │    │(Execute)        │   │
│  └─────────────┘    └───────────────┘    └─────────────────┘   │
│         ↑                  ↑                      ↓             │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                OData Parsers                             │   │
│  │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌───────────────┐  │   │
│  │  │$filter  │ │$expand  │ │$apply   │ │$search        │  │   │
│  │  │Parser   │ │Parser   │ │Parser   │ │Parser         │  │   │
│  │  └─────────┘ └─────────┘ └─────────┘ └───────────────┘  │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

#### DynamicRepository Operations

| Method | SQL Operation |
|--------|---------------|
| `CreateAsync` | INSERT with RETURNING |
| `GetByIdAsync` | SELECT WHERE id = |
| `GetAllAsync` | SELECT * |
| `QueryAsync` | SELECT with WHERE |
| `UpdateAsync` | UPDATE (or temporal version) |
| `DeleteAsync` | DELETE or soft-delete |

#### Business Rules Engine

```
Rule Execution Flow:
┌────────────────────────────────────────────────────────────┐
│                    Before Create/Update                     │
│  1. Load rules for entity (from cache)                      │
│  2. Execute validation rules                                │
│     • If error severity → reject operation                  │
│     • If warning → log and continue                         │
│  3. Execute computation rules                               │
│     • Calculate computed field values                       │
│     • Merge into request data                               │
│  4. Execute conditional (when/else) rules                   │
└────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────────┐
│                    Database Operation                       │
│  • INSERT / UPDATE / DELETE                                 │
│  • Temporal handling if applicable                          │
└────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────────┐
│                    After Create/Update                      │
│  1. Execute after-rules (fire-and-forget)                   │
│  2. Publish domain event                                    │
│     • EntityCreated / EntityUpdated / EntityDeleted         │
│  3. Notify event handlers                                   │
└────────────────────────────────────────────────────────────┘
```

#### Authorization Flow

```
┌────────────────────────────────────────────────────────────┐
│                Permission Check Flow                        │
│                                                             │
│  1. Get access rules for entity                             │
│  2. For each rule:                                          │
│     ├─ Match operation (READ, CREATE, UPDATE, DELETE)       │
│     ├─ Match principal (role, user, authenticated)          │
│     └─ Evaluate WHERE condition (if present)                │
│  3. Decision:                                               │
│     ├─ DENY matched → REJECT (fail-secure)                  │
│     ├─ GRANT matched → ALLOW                                │
│     └─ No match → REJECT (default deny)                     │
│  4. Apply field restrictions (mask/hide)                    │
└────────────────────────────────────────────────────────────┘
```

---

### 3.7 API Layer

**Projects**: `BMMDL.Registry.Api`, `BMMDL.Runtime.Api`

ASP.NET Core REST APIs exposing platform functionality.

#### Runtime API Controllers

| Controller | Responsibility |
|------------|----------------|
| `DynamicEntityController` | OData CRUD operations for entities |
| `DynamicViewController` | OData queries for views |
| `DynamicServiceController` | Service bound operations |
| `ODataServiceController` | Unbound service functions/actions |
| `BatchController` | OData `$batch` multipart requests |
| `AsyncOperationController` | Long-running async operations |
| `ODataMetadataController` | `$metadata` document generation |
| `SequenceController` | ID sequence generation |
| `AuthController` | JWT authentication, refresh tokens |
| `UserController` | User management operations |
| `TenantController` | Tenant management |
| `RuntimeAdminController` | Cache refresh, health operations |
| `HealthController` | Liveness/readiness probes |

#### Runtime API Endpoints

| Route | Method | Description |
|-------|--------|-------------|
| `/api/odata/{module}/{entity}` | GET | List with OData query |
| `/api/odata/{module}/{entity}/{id}` | GET | Get by ID |
| `/api/odata/{module}/{entity}/{id}/versions` | GET | Get all versions (temporal) |
| `/api/odata/{module}/{entity}` | POST | Create (supports deep insert) |
| `/api/odata/{module}/{entity}/{id}` | PATCH | Update (supports deep update) |
| `/api/odata/{module}/{entity}/{id}` | DELETE | Delete |
| `/api/odata/{module}/{entity}/{id}/$ref` | GET/PUT/DELETE | Association management |
| `/api/odata/$batch` | POST | Batch operations (multipart) |
| `/api/odata/$metadata` | GET | OData metadata document |
| `/api/services/{module}/{service}/{operation}` | POST | Service operation |
| `/api/auth/login` | POST | JWT authentication |
| `/api/auth/refresh` | POST | Refresh access token |
| `/api/sequences/{name}/next` | POST | Get next sequence value |
| `/api/async/{operationId}` | GET | Poll async operation status |
| `/api/admin/cache/refresh` | POST | Force meta-model cache refresh |

#### OData Query Support

| Parameter | Example | Description |
|-----------|---------|-------------|
| `$filter` | `status eq 'Active'` | WHERE clause |
| `$select` | `id,name,email` | Column selection |
| `$expand` | `customer,items($levels=3)` | JOIN relationships (recursive support) |
| `$orderby` | `createdAt desc` | ORDER BY |
| `$top` | `10` | LIMIT |
| `$skip` | `20` | OFFSET |
| `$count` | `true` | Include total count |
| `$search` | `john` | Full-text search |
| `$apply` | `groupby((status),aggregate($count as count))` | Aggregation |
| `$compute` | `fullName as concat(firstName,' ',lastName)` | Computed properties |
| `$deltatoken` | `token123` | Delta/incremental sync |
| `asOf` | `2024-01-15T10:30:00Z` | Point-in-time query (temporal) |
| `validAt` | `2024-01-15` | Valid-time query (bitemporal) |
| `includeHistory` | `true` | Include all temporal versions |

#### Middleware Pipeline

```
Request
   │
   ▼
┌─────────────────────────┐
│ 1. ExceptionMiddleware  │  Global error handling
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ 2. MetricsMiddleware    │  Request timing (OpenTelemetry)
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ 3. CORS                 │  Cross-origin requests
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ 4. ODataHeaderMiddleware│  OData-Version header
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ 5. Authentication       │  JWT validation
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ 6. TenantContext        │  Extract tenant from token
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ 7. Authorization        │  Access control checks
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ 8. Controller Routing   │  Route to endpoint
└─────────────────────────┘
```

---

## 4. Cross-Cutting Concerns

### 4.1 Multi-Tenancy

BMMDL provides built-in multi-tenant isolation at all layers.

```
┌────────────────────────────────────────────────────────────┐
│                    Tenant Isolation                         │
│                                                             │
│  JWT Token                                                  │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ { "tenantId": "abc123", "userId": "...", ... }       │  │
│  └──────────────────────────────────────────────────────┘  │
│                           │                                 │
│                           ▼                                 │
│  TenantContextMiddleware extracts tenantId                  │
│                           │                                 │
│                           ▼                                 │
│  TenantConnectionFactory provides tenant-specific connection│
│                           │                                 │
│                           ▼                                 │
│  DynamicSqlBuilder adds: WHERE tenant_id = @tenantId        │
│  (for all tenant-scoped entities)                           │
│                           │                                 │
│                           ▼                                 │
│  PostgreSQL enforces row-level isolation                    │
└────────────────────────────────────────────────────────────┘
```

### 4.2 Temporal Data Support

BMMDL supports bitemporal data with two strategies:

#### InlineHistory Strategy

All versions stored in the same table:

```sql
CREATE TABLE orders (
    id UUID PRIMARY KEY,
    order_number VARCHAR(50),
    -- ... other fields ...
    system_start TIMESTAMPTZ NOT NULL DEFAULT now(),
    system_end TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz
);

-- Current record: system_end = 'infinity'
-- Update creates new version:
--   1. UPDATE SET system_end = now() WHERE id = @id AND system_end = 'infinity'
--   2. INSERT new record with system_start = now(), system_end = 'infinity'
```

#### SeparateTables Strategy

Main table + history table:

```sql
CREATE TABLE orders (
    id UUID PRIMARY KEY,
    order_number VARCHAR(50),
    system_start TIMESTAMPTZ NOT NULL,
    system_end TIMESTAMPTZ NOT NULL
);

CREATE TABLE orders_history (
    id UUID,
    order_number VARCHAR(50),
    system_start TIMESTAMPTZ NOT NULL,
    system_end TIMESTAMPTZ NOT NULL
);

-- Trigger copies old version to history on UPDATE
```

#### Temporal Queries

```
GET /api/odata/Sales/Order?asOf=2024-01-15T10:30:00Z
    → Returns record as it existed at that moment

GET /api/odata/Sales/Order/{id}/versions
    → Returns all historical versions

GET /api/odata/Sales/Order?validAt=2024-01-15
    → Returns records valid at that business date (bitemporal)
```

### 4.3 Event-Driven Architecture

```
┌────────────────────────────────────────────────────────────┐
│                    Event Publishing                         │
│                                                             │
│  After CRUD Operation                                       │
│         │                                                   │
│         ▼                                                   │
│  EventPublisher.PublishAsync()                              │
│         │                                                   │
│         ├──→ AuditLogEventHandler (logs event)              │
│         │                                                   │
│         ├──→ ServiceEventHandler (routes to service)        │
│         │                                                   │
│         └──→ Custom handlers (registered at startup)        │
│                                                             │
│  Event Types:                                               │
│  • EntityCreated { entityName, data }                       │
│  • EntityUpdated { entityName, data, changedFields }        │
│  • EntityDeleted { entityName, id, timestamp }              │
└────────────────────────────────────────────────────────────┘
```

### 4.4 Query Optimization

```
┌────────────────────────────────────────────────────────────┐
│                    QueryPlanCache                           │
│                                                             │
│  Key: Operation|Entity|Filter|OrderBy|Select|Search|...     │
│  Value: (SQL string, NpgsqlParameter[])                     │
│                                                             │
│  • LRU cache with 1000 entries (configurable)               │
│  • Populated on SELECT queries                              │
│  • Skipped for queries with dynamic IDs                     │
│  • Thread-safe concurrent dictionary                        │
│                                                             │
│  Benefits:                                                  │
│  • Avoids SQL generation overhead                           │
│  • Parameter reuse                                          │
│  • Consistent query plans                                   │
└────────────────────────────────────────────────────────────┘
```

---

## 5. DSL Language Reference

### 5.1 Entity Definition

```bmmdl
@Core.Description: 'Customer master data'
@Temporal.Strategy: 'inline'
entity Customer : Auditable, TenantAware {
    key ID: UUID;

    // Basic fields
    code: String(20) not null;
    name: String(100) not null;
    email: String(255);
    status: CustomerStatus default #Active;

    // Computed fields
    virtual orderCount: Integer computed = count(orders);
    totalSpent: Decimal(15,2) stored = sum(orders.totalAmount);

    // Relationships
    orders: composition [*] of Order;
    account: association [0,1] to Account;

    // Indexes
    index idx_code on (code);
    unique index idx_email on (email);

    // Constraints
    constraint chk_email check (email like '%@%.%');
}
```

### 5.2 Business Rules

```bmmdl
rule ValidateOrder for Order on before create {
    // Validation
    validate totalAmount >= 0
        message 'Total amount cannot be negative'
        severity error;

    validate count(items) > 0
        message 'Order must have at least one item'
        severity error;
}

rule CalculateOrderTotals for Order on before create, before update {
    // Computation
    compute subtotal = sum(items.amount);
    compute taxAmount = subtotal * 0.1;
    compute totalAmount = subtotal + taxAmount;
}

rule OrderStatusChange for Order on after update {
    // Conditional logic
    when status = 'Confirmed' and previous.status = 'Draft' then {
        call NotifyCustomer(customerId);
    }
}
```

### 5.3 Access Control

```bmmdl
access control for Customer {
    // Role-based grants
    grant read to authenticated;
    grant create, update to role 'Sales', 'SalesManager';
    grant delete to role 'Admin';

    // Row-level security
    grant read, update to role 'Sales'
        where assignedSalesRep.ID = $userId;

    // Field restrictions
    restrict fields {
        creditLimit: visible when $hasFinanceRole;
        email: masked;
        costPrice: hidden when not $hasFinanceRole;
    };
}
```

### 5.4 Services

```bmmdl
service OrderService {
    // Exposed entities
    entity Orders as Order;
    entity OrderItems as OrderItem;

    // Functions (read-only)
    function getOrdersByStatus(status: OrderStatus) returns Order;
    function calculateShipping(orderId: UUID) returns Decimal;

    // Actions (can modify state)
    action confirmOrder(orderId: UUID) returns Order
        requires Order.status = 'Draft'
        ensures Order.status = 'Confirmed'
        emits OrderConfirmed;

    action cancelOrder(orderId: UUID, reason: String) returns Order;
}
```

### 5.5 Sequences

```bmmdl
sequence OrderNumberSeq {
    pattern: 'ORD-{company}-{year}{month}-{seq:5}';
    start: 1;
    increment: 1;
    padding: 5;
    scope: company;
    reset on monthly;
}
```

### 5.6 Events

```bmmdl
event OrderConfirmed {
    orderId: UUID;
    orderNumber: String(20);
    customerId: UUID;
    totalAmount: Decimal(15,2);
    confirmedAt: DateTime;
    confirmedBy: UUID;
}
```

### 5.7 Migrations

```bmmdl
migration 'AddCustomerEmail' {
    version: '1.1.0';
    author: 'developer@example.com';
    description: 'Add email field';
    breaking: false;

    up {
        alter entity Customer {
            add email: String(255);
        }
    }

    down {
        alter entity Customer {
            drop column email;
        }
    }
}
```

---

## 6. Deployment Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Production Deployment                         │
│                                                                      │
│  ┌──────────────────┐     ┌──────────────────┐                      │
│  │  Load Balancer   │     │  Load Balancer   │                      │
│  │  (Registry API)  │     │  (Runtime API)   │                      │
│  └────────┬─────────┘     └────────┬─────────┘                      │
│           │                        │                                 │
│     ┌─────┴─────┐            ┌─────┴─────┐                          │
│     ▼           ▼            ▼           ▼                          │
│  ┌─────┐     ┌─────┐     ┌─────┐     ┌─────┐                        │
│  │ API │     │ API │     │ API │     │ API │                        │
│  │ Pod │     │ Pod │     │ Pod │     │ Pod │                        │
│  └──┬──┘     └──┬──┘     └──┬──┘     └──┬──┘                        │
│     │           │           │           │                            │
│     └─────┬─────┘           └─────┬─────┘                            │
│           │                       │                                  │
│           ▼                       ▼                                  │
│  ┌─────────────────┐     ┌─────────────────┐                        │
│  │ PostgreSQL      │     │ PostgreSQL      │                        │
│  │ (Registry)      │     │ (Platform)      │                        │
│  │ Primary/Replica │     │ Primary/Replica │                        │
│  └─────────────────┘     └─────────────────┘                        │
│                                                                      │
│  Monitoring:                                                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                  │
│  │ Prometheus  │  │ Grafana     │  │ Jaeger      │                  │
│  │ (/metrics)  │  │ (dashboards)│  │ (tracing)   │                  │
│  └─────────────┘  └─────────────┘  └─────────────┘                  │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 7. Technology Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Runtime | .NET | 10.0 |
| Database | PostgreSQL | 15+ |
| ORM | Entity Framework Core | 10.0.1 / 10.0.2 |
| DB Driver | Npgsql | 10.0.0 / 10.0.1 |
| Parser | ANTLR4 | 4.13.1 |
| Web Framework | ASP.NET Core | 10.0 |
| Authentication | JWT Bearer | 10.0.1 |
| OAuth | Google.Apis.Auth | 1.73.0 |
| Observability | OpenTelemetry | 1.11.x |
| Metrics | Prometheus | 1.11.2-beta |
| CLI Framework | System.CommandLine | 2.0.0-beta4 |
| Testing | xUnit | 2.6.4 |
| Assertions | FluentAssertions | 6.12.0 |
| Test SDK | Microsoft.NET.Test.Sdk | 17.8.0 |
| Test Containers | Testcontainers | Latest |

---

## 8. Security Considerations

### Authentication
- JWT tokens with configurable expiration
- Google OAuth integration
- Refresh token support

### Authorization
- Role-based access control (RBAC)
- Row-level security via WHERE conditions
- Field-level restrictions (mask, hide, readonly)
- Default deny policy

### Data Protection
- Parameterized queries (SQL injection prevention)
- Input validation at API layer
- Field masking for sensitive data
- Audit logging via events

### Multi-Tenancy
- Tenant isolation at database level
- Connection pooling per tenant
- No cross-tenant data access

---

## 9. Performance Considerations

### Query Optimization
- Query plan caching (LRU, 1000 entries)
- Parameterized queries for plan reuse
- Index recommendations in meta-model

### Caching
- MetaModelCache for O(1) entity lookups
- In-memory symbol table
- Connection pooling (Npgsql)

### Scalability
- Stateless API design
- Horizontal scaling via load balancer
- Read replicas for queries

### Observability
- OpenTelemetry metrics
- Prometheus export
- Structured logging
- Request timing

---

## 10. Project Structure

### Source Projects (src/)

| Project | Purpose |
|---------|---------|
| `BMMDL.MetaModel` | Core domain model (zero dependencies) |
| `BMMDL.Parser` | ANTLR4-generated parser classes |
| `BMMDL.Compiler` | 12-pass compilation pipeline |
| `BMMDL.CodeGen` | PostgreSQL DDL generation |
| `BMMDL.Registry` | Meta-model persistence (EF Core) |
| `BMMDL.SchemaManager` | Database schema operations |
| `BMMDL.Runtime` | Execution engine, CRUD, rules |
| `BMMDL.Registry.Api` | REST API for registry operations |
| `BMMDL.Runtime.Api` | REST API for runtime (OData v4) |
| `BMMDL.Tests` | Unit tests |
| `BMMDL.Tests.New` | E2E tests (current, with improved fixtures) |
| `BMMDL.Tests.E2E` | E2E tests (legacy, deprecated) |

### ERP Modules (erp_modules/)

Pre-built ERP domain modules demonstrating BMMDL capabilities:

| # | Module | Description | Dependencies |
|---|--------|-------------|--------------|
| 00 | **platform** | Base platform infrastructure | _(none)_ |
| 01 | **core** | Foundation types, aspects, enums | platform |
| 02 | **master_data** | Employee, Customer, Product, Vendor | core |
| 03 | **hr** | Leave, Performance, Training | master_data |
| 04 | **finance** | Payroll configuration and processing | hr |
| 05 | **scm** | Sales, Purchasing, Inventory, Manufacturing | master_data |
| 06 | **rules** | Business validations for all domains | hr, finance, scm |
| 07 | **services** | API services for all domains | rules |
| 08 | **security** | Access control policies | services |
| 09 | **workflow** | Workflow & process automation | security |
| 10 | **config_management** | Configuration & settings | workflow |
| 11 | **warehouse** | Warehouse management system | scm |

**Installation Order**:
```
00_platform → 01_core → 02_master_data → 03_hr → 04_finance ─┐
                                       └→ 05_scm ────────────────┴→ 06_rules → 07_services → 08_security → 09_workflow → 10_config → 11_warehouse
```

### Sample Files (samples/)

Example BMMDL files for common business domains:
- E-commerce (entities, rules, services, security)
- HR (master, leave, performance)
- Manufacturing (master, production, types)
- Advanced features demonstration
- File storage showcase

---

## 11. Future Considerations

### Planned Features
- GraphQL API layer
- Real-time subscriptions (WebSocket)
- Distributed caching (Redis)
- Event sourcing option
- Multi-region deployment

### Extension Points
- Custom functions in FunctionRegistry
- Custom event handlers
- Custom validation passes
- Custom code generators

---

## 12. Related Documentation

| Document | Location | Description |
|----------|----------|-------------|
| Project Guide | `CLAUDE.md` | Quick start, commands, conventions |
| OData v4 Plan | `docs/ODATA_V4_COMPLETION_PLAN.md` | OData feature roadmap |
| OData Metadata | `docs/odata_metadata_sample.xml` | Sample metadata document |
| Service Mapping | `docs/architecture/odata_v4_service_mapping.md` | OData service patterns |
| AOT Readiness | `docs/aot_readiness_audit.md` | Native AOT analysis |
| Asset Valuation | `docs/technology_asset_valuation.md` | Technology value analysis |
| Test Patterns | `docs/architecture/README.md` | E2E testing documentation |
| ERP Modules | `erp_modules/README.md` | Module system overview |

---

*Last updated: 2026-02-03*
