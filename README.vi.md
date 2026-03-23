# BMMDL

**Nền tảng full-stack biên dịch định nghĩa miền nghiệp vụ thành schema cơ sở dữ liệu và REST API sẵn sàng triển khai.**

BMMDL là một hệ thống runtime hoàn chỉnh—không chỉ là một trình biên dịch. Viết logic nghiệp vụ một lần bằng DSL khai báo, và nhận về cơ sở dữ liệu PostgreSQL sẵn sàng triển khai, OData v4 REST API, business rule engine, plugin architecture, và metadata layer có thể truy vấn—tất cả đều tích hợp sẵn multi-tenancy, authentication, temporal data, và event-driven architecture.

[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple?style=flat-square)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/Tests-3500%2B%20Passing-green?style=flat-square)](#)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Powered-blue?style=flat-square)](https://www.postgresql.org/)
[![OData v4](https://img.shields.io/badge/OData-v4%20Full-orange?style=flat-square)](https://www.odata.org/)
[![Vue 3](https://img.shields.io/badge/Vue-3%20%2B%20TypeScript-42b883?style=flat-square)](https://vuejs.org/)

---

## Những Gì Bạn Nhận Được

Viết code này:

```bmmdl
module ECommerce version '1.0' {
    description "E-Commerce module";
    namespace business.ecommerce {

        entity Order : Auditable {
            key ID: UUID;
            customer: Association to Customer;
            subtotal: Decimal computed sum(items.lineTotal) stored;
            totalAmount: Decimal computed subtotal + taxAmount;
            items: Composition of OrderItem;
        }

        rule OrderValidation for Order {
            on before CREATE, UPDATE {
                validate totalAmount > 0 message 'Total must be positive';
                compute discount = totalAmount > 1000 ? totalAmount * 0.1 : 0;
            }
            on after CREATE {
                emit OrderCreated { orderId: $current.ID };
            }
        }
    }
}
```

**Nhận được tự động:**

- **PostgreSQL Database Schema** — Tables, indexes, foreign keys, RLS policies, computed field triggers
- **OData v4 REST API** — Full CRUD với `$filter`, `$expand`, `$orderby`, `$select`, `$top/$skip`, `$count`, `$search`, `$apply`, `$compute`, `$batch`, `$ref`, `$value`, ETag concurrency, deep insert/update, delta responses, singleton entities, bound/unbound actions
- **Business Rule Engine** — Validation, computed fields, pre/post hooks, reject statements
- **Event System** — Domain events với emit/emits, outbox pattern, retry + dead letter queue, integration broker adapter, observability (CausationId chain)
- **Plugin Architecture** — 13 built-in features, external plugin loading via DLL, atomic registry rebuild
- **Multi-Tenancy** — Automatic tenant isolation với JWT, RLS policies, compiler enforcement
- **Authorization** — Role-based permissions (GRANT/DENY), field-level security (Hidden/Masked/Readonly)
- **Temporal Data** — Bitemporal support (transaction + valid time), time-travel queries
- **Vue 3 Frontend** — Admin UI, entity CRUD, plugin management, form layout designer

---

## Tại Sao Chọn BMMDL?

| Bạn Muốn | Traditional Stack | BMMDL |
|----------|------------------|-------|
| **Định nghĩa schema** | Viết SQL migrations | Viết file `.bmmdl` |
| **Xây dựng REST API** | Code controllers, DTOs, validation | Automatic OData v4 API |
| **Multi-tenancy** | Manual RLS policies, composite FKs | First-class language support |
| **Business rules** | Rải rác khắp services | Declarative rules trong DSL |
| **Event system** | Manual pub/sub setup | `emit`/`emits` keywords + outbox |
| **Plugin system** | Custom plugin framework | Built-in extensible architecture |
| **Temporal data** | Complex trigger logic | `@Temporal` annotation |
| **File storage** | Manual S3 integration | `FileReference` type |
| **Entity inheritance** | Manual table-per-type | `entity Child : Parent` |
| **Query metadata** | Mất tại runtime | Lưu trong PostgreSQL |
| **Time to production** | Nhiều tuần | Vài giờ |

---

## Kiến Trúc

### Full Platform Stack

```
┌──────────────────────────────────────────────────────────────┐
│ FRONTEND (Vue 3 + TypeScript, Port 5173)                     │
│ • Entity CRUD Views        • Admin Module Management         │
│ • Plugin Management UI     • Form Layout Designer            │
│ • Dashboard & Analytics    • Smart Table Components          │
└──────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────┐
│ BMMDL RUNTIME API (Port 5175)                                │
│ • OData v4 Controllers      • JWT Authentication             │
│ • Rule Engine + Events      • Tenant Context Middleware      │
│ • Dynamic SQL Generation    • Plugin Pipeline                │
│ • Temporal Query Support    • Unit of Work Transactions      │
│ • Deep Insert/Update        • $batch, $ref, $value           │
│ • Delta Responses           • Async Operations               │
└──────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────┐
│ PLUGIN SYSTEM (13 Built-in Features + External Plugins)      │
│ • MultiTenancy, Identity, Audit, SoftDelete, Localization    │
│ • Temporal, FileReference, HasStream, Encryption, CDC        │
│ • Sequence, Inheritance, Reporting                           │
│ • External: DLL loading via PluginDirectoryLoader            │
│ • Pipeline: Query filters, Write hooks, Metadata contrib.    │
└──────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────┐
│ BMMDL REGISTRY API (Port 51742)                              │
│ • Module Compilation        • Schema Management              │
│ • Meta-Model Persistence    • Admin Operations               │
└──────────────────────────────────────────────────────────────┘
                              ↓
┌──────────────────────────────────────────────────────────────┐
│ PostgreSQL                                                    │
│ • registry schema: compiled meta-model (entities, fields,    │
│   rules, access controls, expressions, modules)             │
│ • platform schema: runtime data (business entities,          │
│   tenants, users, roles, audit logs, event outbox)           │
└──────────────────────────────────────────────────────────────┘
```

### 12-Pass Compiler Pipeline

```
Pass 1  → Lexical Analysis              (tokenization)
Pass 2  → Syntactic Analysis            (parse tree)
Pass 3  → Model Building                (AST construction)
Pass 4  → Symbol Resolution             (name binding)
Pass 5  → Dependency Graph              (circular entity detection)
Pass 6  → Expression Dependency          (circular field detection)
Pass 7  → Binding & Type Inference       (type checking)
Pass 8  → Tenant Isolation Validation    (tenant rules enforcement)
Pass 9  → File Storage Validation        (storage annotation rules)
Pass 10 → Temporal Validation            (temporal data rules)
Pass 11 → Semantic Validation            (business rule validation)
Pass 12 → Optimization                   (aspect inlining, deduplication)
         + Feature Contribution          (plugin pipeline integration)
         + Annotation Merge              (cross-module annotations)
         + Extension Merge               (cross-module entity extension)
         + Inheritance Resolution        (table-per-type inheritance)
         + Modification Pass             (field rename/remove/type change)
         + Plugin Annotation Validation  (plugin annotation schema check)
         ↓
PostgreSQL DDL Generation + Metadata Persistence
```

---

## Tính Năng Cốt Lõi

### Full Runtime System

Không chỉ là trình biên dịch. BMMDL bao gồm runtime sẵn sàng triển khai với:

- **OData v4 REST API** (full spec) — `$filter`, `$expand` (+ `$levels` recursive), `$orderby`, `$select`, `$top/$skip`, `$count`, `$search`, `$apply`, `$compute`, `$value`, `$batch`, `$ref`, `$metadata` (full CSDL), deep insert/update, ETag concurrency, delta responses, singleton entities, bound/unbound actions, containment, async operations, capability annotations, `Prefer` header
- **Dynamic SQL Generation** — Type-safe parameterized queries, LEFT JOIN cho ManyToOne, sub-queries cho OneToMany, ManyToMany junction table JOINs
- **Business Rule Engine** — Before/after triggers, validate/compute/reject statements, `callStmt`/`foreachStmt`/`letStmt`, aggregate expressions
- **Event System** — `emit`/`emits` keywords, domain event publisher, outbox pattern (durable delivery), retry + dead letter queue, integration broker adapter, CausationId/CorrelationId tracing, event metrics
- **Plugin Architecture** — 13 built-in features, external DLL plugin loading, isolated AssemblyLoadContext, atomic registry rebuild, plugin manifest system
- **Unit of Work** — Scoped transactions, post-commit event dispatch, connection-per-request
- **Authorization** — Role-based permissions (GRANT/DENY), field-level security (Hidden/Masked/Readonly), WHERE condition evaluation
- **Expression Evaluator** — Runtime evaluation of compiled BMMDL expression ASTs

### First-Class Multi-Tenancy

Multi-tenancy là core language feature với compiler enforcement:

```bmmdl
module HumanResources version '1.0' {
    tenant-aware: true;  // Tất cả entities tự động scoped

    namespace hr {
        entity Employee {
            key ID: UUID;
            name: String(200);
            // tenantId tự động thêm bởi compiler
        }

        @GlobalScoped  // Opt-out cho reference data
        entity Country {
            key Code: String(3);
            name: String(100);
        }

        access control for Employee {
            grant READ to role 'User' at TENANT scope;
            grant UPDATE to role 'Manager' at COMPANY scope;
            grant ALL to role 'Admin' at GLOBAL scope;
        }
    }
}
```

**Compiler tự động sinh:**
- PostgreSQL Row-Level Security policies
- Composite foreign keys cho tenant isolation
- Tenant-scoped unique indexes
- 7 validation rules (TENANT001-TENANT007)

### Entity Inheritance (Table-Per-Type)

```bmmdl
entity Vehicle {
    key ID: UUID;
    make: String(100);
    model: String(100);
    year: Integer;
}

entity Car : Vehicle {
    doors: Integer;
    trunkSize: Decimal;
}

entity Truck : Vehicle {
    payloadCapacity: Decimal;
    axles: Integer;
}
```

Polymorphic GET, multi-table INSERT, automatic JOIN resolution.

### Extend & Modify Entities

```bmmdl
// Cross-module entity extension
extend entity Customer {
    loyaltyPoints: Integer default 0;
    memberSince: Date;
}

// Schema-safe modification
modify entity Product {
    rename oldName to displayName;
    remove deprecatedField;
    change price: Decimal(18,4);  // Type change with compatibility check
}
```

### Temporal Data (Time-Travel Queries)

```bmmdl
@Temporal
@Temporal.Strategy: 'InlineHistory'
@Temporal.From: 'validFrom'
@Temporal.To: 'validTo'
entity Product {
    key ID: UUID;
    name: String(200);
    price: Decimal;
}
```

```bash
# Time-travel query
GET /api/odata/Catalog/Product/123?asOf=2026-01-01T00:00:00Z

# Query valid at specific date
GET /api/odata/Catalog/Product?validAt=2026-06-15

# All versions
GET /api/odata/Catalog/Product/123?includeHistory=true
```

### Event-Driven Architecture

```bmmdl
event OrderCreated {
    orderId: UUID;
    customerId: UUID;
    totalAmount: Decimal;
}

rule OrderWorkflow for Order {
    on after CREATE {
        emit OrderCreated {
            orderId: $current.ID,
            customerId: $current.CustomerId,
            totalAmount: $current.TotalAmount
        };
    }
}

service OrderService {
    on OrderCreated {
        // Service event handler
        call NotificationService.sendConfirmation(orderId: event.orderId);
    }
}
```

**Infrastructure:**
- Outbox pattern (durable delivery, atomic with business transaction)
- Exponential backoff retry (2^n seconds)
- Dead letter queue for failed events
- Integration broker adapter (RabbitMQ, Kafka-ready)
- CausationId chain for distributed tracing
- Event metrics (5 counters) via `IEventMetrics`

### Plugin Architecture

13 built-in features, mỗi feature đều implement `IPlatformFeature`:

| Plugin | Stage | Mô tả |
|--------|-------|--------|
| MultiTenancyPlugin | -1 | Tenant isolation, System Tenant seeding |
| PlatformIdentityPlugin | 0 | Users, roles, permissions, default role seeding |
| AuditFieldFeature | 10 | created_at/by, updated_at/by auto-population |
| SoftDeleteFeature | 20 | Logical delete với is_deleted flag |
| LocalizationFeature | 30 | Multi-language text (_texts tables) |
| TemporalFeature | 40 | Bitemporal version tracking |
| FileReferenceFeature | 50 | External file storage metadata |
| HasStreamFeature | 51 | Media entity streaming (ETag concurrency) |
| FieldEncryptionFeature | 55 | AES-256 field encryption |
| ChangeDataCaptureFeature | 56 | Change tracking cho audit |
| SequenceFeature | 60 | Auto-increment sequences |
| InheritanceFeature | 65 | Table-per-type entity inheritance |
| ReportingPlugin | 95 | Report template management |

**External plugin loading:**
```
plugins/
├── my-plugin/
│   ├── plugin.json          # Manifest (name, version, dependencies)
│   └── MyPlugin.dll         # Plugin assembly
```

Plugins are loaded via isolated `PluginAssemblyLoadContext` (collectible), discovered automatically, and atomically merged into the feature registry.

**Plugin interfaces:** `IPlatformFeature`, `IPluginLifecycle`, `IBmmdlModuleProvider`, `IPlatformEntityProvider`, `IAdminPageProvider`, `IMenuContributor`, `ISettingsProvider`, `IFeatureQueryFilter`, `IFeatureWriteHook`, `IFeatureMetadataContributor`, `IFeatureInsertContributor`, `IFeatureUpdateContributor`, `IFeatureDeleteStrategy`, `IGlobalFeature`

### Large File Storage

```bmmdl
entity Document {
    key ID: UUID;
    name: String(255);

    @Storage.Provider: 'S3'
    @Storage.Bucket: 'documents'
    @Storage.MaxSize: 10485760
    @Storage.AllowedTypes: ['application/pdf', 'image/*']
    attachment: FileReference;
    // Expands to 8 metadata columns: provider, bucket, key, size, mime_type, checksum, uploaded_at, uploaded_by
}
```

### Authorization & Field-Level Security

```bmmdl
access control for Employee {
    grant READ to role 'User' at TENANT scope;
    grant UPDATE to role 'Manager' at TENANT scope
        where department.managerId = $current_user.id;
    deny DELETE to role 'User';
    grant DELETE to role 'Admin' at GLOBAL scope;

    restrict fields {
        salary: Hidden for role 'User';
        email: Masked('email') for role 'External';
        ssn: Masked('partial', 3, 4) for authenticated;
    }
}
```

### Computed Fields với Dependency Tracking

```bmmdl
entity Invoice {
    subtotal: Decimal computed sum(lineItems.amount);
    tax: Decimal computed subtotal * 0.1;
    total: Decimal computed subtotal + tax;
    // Compiler tracks: total → tax → subtotal
    // Circular dependencies → compile error
}
```

### Module System

```bmmdl
module ECommerceCore version '1.0' {
    author "DevTeam";
    description "E-Commerce core module";
    depends core version '1.0';
    namespace business.ecommerce { ... }
}
```

**ERP Module Library** (12 modules):
```
common.bmmdl → 00_platform → 01_core → 02_master_data → 03_hr → 04_finance
                                      ↘ 05_scm → 06_rules → 07_services → 08_security → 09_workflow
                                                                          → 10_config_management → 11_warehouse
```

---

## Quick Start

### 1. Build Platform

```bash
git clone https://github.com/phankhanhhung/Mudop.git
cd Mudop
dotnet build BMMDL.sln
```

### 2. Setup PostgreSQL

```bash
docker run -d \
  --name bmmdl-postgres \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 \
  postgres:17
```

### 3. Run Tests

```bash
# Unit tests (~2900 tests)
dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj

# E2E tests (requires PostgreSQL, ~650 tests)
dotnet test src/BMMDL.Tests.New/BMMDL.Tests.New.csproj
```

### 4. Khởi động Services

```bash
# Registry API (module management, port 51742)
dotnet run --project src/BMMDL.Registry.Api

# Runtime API (OData v4 + CRUD + Rules, port 5175)
dotnet run --project src/BMMDL.Runtime.Api

# Frontend (port 5173)
cd frontend && npm install && npm run dev
```

### 5. Truy vấn qua OData v4

```bash
# Authenticate
curl -X POST http://localhost:5175/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "admin123"}'

# Query entities
curl -H "Authorization: Bearer $TOKEN" \
     -H "X-Tenant-ID: 00000000-0000-0000-0000-000000000001" \
     "http://localhost:5175/api/odata/Platform/Tenant"

# With $expand, $filter, $orderby
curl -H "Authorization: Bearer $TOKEN" \
     "http://localhost:5175/api/odata/ECommerce/Order?\$expand=customer,items&\$filter=totalAmount gt 500&\$orderby=createdAt desc&\$top=20"
```

---

## Cấu Trúc Dự Án

```
BMMDL/
├── Grammar/                       # ANTLR4 grammar (1,436 lines)
│   ├── BmmdlLexer.g4             # Lexer rules (tokens, keywords)
│   └── BmmdlParser.g4            # Parser rules (grammar productions)
├── frontend/                      # Vue 3 + TypeScript + Tailwind CSS
│   └── src/
│       ├── components/            # UI components (entity, smart, admin, etc.)
│       ├── composables/           # Vue composables (useAuth, useOData, useTenant, etc.)
│       ├── services/              # API clients (odata, metadata, admin, plugin)
│       ├── plugins/               # Frontend plugin system (registry, dynamic routing)
│       └── views/                 # Pages (entity CRUD, admin, dashboard, settings)
├── erp_modules/                   # 12-module ERP library + common.bmmdl
├── samples/                       # Sample BMMDL files
├── src/
│   ├── BMMDL.MetaModel/          # Core domain model (zero dependencies)
│   ├── BMMDL.Compiler/           # 12-pass compiler pipeline + CLI (bmmdlc)
│   │   └── Pipeline/Passes/      # 18 compilation passes
│   ├── BMMDL.CodeGen/            # PostgreSQL DDL generation
│   ├── BMMDL.Registry/           # Meta-model persistence (EF Core)
│   ├── BMMDL.Registry.Api/       # Registry REST API (port 51742)
│   ├── BMMDL.SchemaManager/      # Database schema operations
│   ├── BMMDL.Runtime/            # Execution engine
│   │   ├── DataAccess/           # DynamicSqlBuilder, UnitOfWork, RowReader
│   │   ├── Rules/                # RuleEngine, StatementExecutor
│   │   ├── Events/               # EventPublisher, OutboxStore, OutboxProcessor
│   │   ├── Authorization/        # PermissionChecker, FieldRestrictionApplier
│   │   ├── Plugins/              # Plugin system
│   │   │   ├── Features/         # 13 built-in features
│   │   │   └── Loading/          # External plugin loader (DLL)
│   │   └── Services/             # Platform services
│   ├── BMMDL.Runtime.Api/        # OData v4 REST API (port 5175)
│   │   ├── Controllers/          # 10 controllers (Entity, Batch, Ref, Action, etc.)
│   │   └── Handlers/             # DeepInsert, DeepUpdate, RecursiveExpand
│   ├── BMMDL.Tests/              # Unit tests (~2900 tests)
│   └── BMMDL.Tests.New/          # E2E tests (~650 tests)
└── docs/                          # Architecture & design documentation
```

---

## DSL Syntax Quick Reference

```bmmdl
// Module definition
module MyApp version '1.0' {
    author "BMMDL Team";
    description "Sample application module";
    depends core version '1.0';

    namespace business.crm {
        // Entity with inheritance, compositions, computed fields
        entity Customer : Auditable {
            key ID: UUID;
            name: String(100);
            email: String(255);
            status: CustomerStatus default #Active;
            orders: composition [*] of Order;
            account: association [0,1] to Account;
            virtual orderCount: Integer computed = count(orders);
        }

        // Enum
        enum CustomerStatus { Active = 1; Inactive = 2; Suspended = 3; }

        // Business rule with emit
        rule ValidateCustomer for Customer on before create {
            validate email like '%@%.%' message 'Invalid email' severity error;
        }

        // Access control
        access control for Customer {
            grant read to authenticated;
            grant create, update to role 'Sales';
            grant delete to role 'Admin';
        }

        // Event definition
        event CustomerCreated { customerId: UUID; name: String; }

        // Service with event handler
        service CustomerService {
            entity Customers as Customer;
            action activate(customerId: UUID) returns Customer;
            on CustomerCreated {
                call NotificationService.welcome(customerId: event.customerId);
            }
        }

        // Entity extension
        extend entity Customer {
            loyaltyPoints: Integer default 0;
        }

        // Migration
        migration AddLoyaltyProgram version '1.1' {
            up { alter table Customer add column loyalty_tier varchar(20); }
            down { alter table Customer drop column loyalty_tier; }
        }
    }
}
```

### Type System

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
| `Array<Type>` | PostgreSQL arrays | e.g. `Array<String>` |
| `localized String(n)` | + `_texts` table | Multi-language |
| `FileReference` | 8 metadata columns | External storage |

---

## Lệnh Thông Dụng

### Build & Test

```bash
# Build
dotnet build BMMDL.sln

# Unit tests
dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj

# E2E tests
dotnet test src/BMMDL.Tests.New/BMMDL.Tests.New.csproj

# Filter by category
dotnet test --filter "FullyQualifiedName~Runtime"
dotnet test --filter "FullyQualifiedName~Compiler"
dotnet test --filter "FullyQualifiedName~Plugin"
```

### Compiler CLI

```bash
# Validate
dotnet run --project src/BMMDL.Compiler -- validate samples/*.bmmdl

# Full pipeline
dotnet run --project src/BMMDL.Compiler -- compile samples/ecommerce.bmmdl --verbose

# Init schema
dotnet run --project src/BMMDL.Compiler -- init-schema

# Bootstrap (init + compile + migrate)
dotnet run --project src/BMMDL.Compiler -- bootstrap samples/*.bmmdl
```

### Frontend

```bash
cd frontend
npm install
npm run dev          # Development (port 5173)
npm run build        # Production build
npm run lint         # ESLint check
```

---

## So Sánh

| Feature | Hasura | Supabase | Prisma | SAP CAP | PostgREST | **BMMDL** |
|---------|--------|----------|--------|---------|-----------|-----------|
| **Schema DSL** | SDL | SQL | Prisma Schema | CDS | SQL | **BMMDL** |
| **Runtime API** | GraphQL | REST + GraphQL | ORM only | OData + REST | REST | **OData v4 Full** |
| **Queryable Metadata** | Limited | Limited | - | Limited | - | **Full** |
| **Multi-Tenancy** | Manual | Manual (RLS) | Manual | Built-in | Manual | **First-class + Compiler** |
| **Business Rules** | - | - | - | Declarative | - | **Rule Engine** |
| **Event System** | Limited | Webhooks | - | - | - | **Outbox + Broker** |
| **Plugin System** | - | - | - | - | - | **13 Features + DLL** |
| **Entity Inheritance** | - | - | - | - | - | **Table-per-type** |
| **Extend/Modify** | - | - | - | CDS extend | - | **extend + modify** |
| **Computed Fields** | Manual | Manual | Manual | Auto-gen | - | **Auto + Triggers** |
| **Temporal Data** | - | Manual | Manual | - | - | **@Temporal** |
| **File Storage** | - | Storage API | - | Manual | - | **FileReference** |
| **Field Encryption** | - | - | - | - | - | **AES-256** |
| **Module System** | - | - | - | Versioned | - | **Versioned + Deps** |
| **Frontend** | Console | Dashboard | Studio | - | - | **Vue 3 Admin** |
| **Authorization** | JWT | RLS | - | Declarative | - | **RBAC + Field Security** |

---

## Environment Variables

```bash
# Database
REGISTRY_CONNECTION_STRING=Host=localhost;Database=bmmdl_registry;Username=postgres;Password=postgres
PLATFORM_CONNECTION_STRING=Host=localhost;Database=bmmdl_platform;Username=postgres;Password=postgres

# JWT
JWT_SECRET=your-secret-key
JWT_ISSUER=bmmdl
JWT_AUDIENCE=bmmdl-api

# Admin API key (header: X-Admin-Key)
ADMIN_API_KEY=bmmdl-admin-key-change-me

# Logging
ASPNETCORE_ENVIRONMENT=Development
```

---

## Trạng Thái

**3500+ tests passing** (2900+ unit, 650+ E2E)

**Tính năng đã hoàn thành:**
- 12-pass compiler pipeline (18 pass files) với ANTLR4 grammar (1,436 lines)
- Full OData v4 REST API (20+ spec features)
- Business Rule Engine với validate/compute/reject/emit
- Event-Driven Architecture (outbox, retry, dead letter, broker adapter, tracing)
- Plugin Architecture (13 built-in features + external DLL loading)
- Entity Inheritance (table-per-type) + Extend Entity + Modify Entity
- Multi-tenant data isolation với RLS + compiler enforcement
- Temporal queries (time-travel, version history, bitemporal)
- Authorization: RBAC + field-level security (Hidden/Masked/Readonly)
- Unit of Work transactions + post-commit event dispatch
- Large file storage (S3/GCS/MinIO/AzureBlob) via FileReference
- Module system (12 ERP modules) với dependency resolution
- Vue 3 Frontend: entity CRUD, admin, plugin management, form layout designer
- Computed field triggers với dependency tracking + circular detection
- Array types, subquery/EXISTS expressions, aggregate expressions
- Migration definitions (UP/DOWN blocks)
- Abstract entities, cross-aspect views (UNION ALL)
- 13 built-in functions (STDDEV, VARIANCE, INSTR, DECODE, IFNULL, etc.)

---

## Tài Liệu

| Document | Mô Tả |
|----------|--------|
| [**CLAUDE.md**](CLAUDE.md) | Comprehensive codebase reference |
| [**plugin-architecture-plan.md**](docs/plugin-architecture-plan.md) | Plugin system design |
| [**consolidated-roadmap.md**](docs/consolidated-roadmap.md) | Feature roadmap (all complete) |
| [**refactoring-tracker.md**](docs/refactoring-tracker.md) | Refactoring progress |
| [**TENANT_FIRST_CLASS.md**](docs/TENANT_FIRST_CLASS.md) | Multi-tenancy guide |
| [**FILE_STORAGE_DESIGN.md**](docs/FILE_STORAGE_DESIGN.md) | File storage design |

---

## Tech Stack

- **.NET 10.0** — Modern C# với latest language features
- **ANTLR4** — Parser generation (1,436 lines of grammar)
- **PostgreSQL 15+** — Primary persistence layer
- **Entity Framework Core 10.0** — Database access
- **Npgsql 10.0** — PostgreSQL driver
- **Vue 3 + TypeScript** — Frontend SPA
- **Tailwind CSS** — UI styling
- **xUnit + FluentAssertions** — Testing framework
- **Testcontainers** — Database integration tests

---

## Đóng Góp

BMMDL mở cho các đóng góp. Xem [CLAUDE.md](CLAUDE.md) để hiểu codebase conventions.

**Key conventions:**
- Tất cả domain model classes prefix với `Bm` (e.g., `BmEntity`, `BmExpression`)
- Namespace khớp với folder structure
- 100% test pass rate trước khi merge
- Sử dụng `ILogger` (không dùng `Console.WriteLine`)
- Parameterized queries (không string interpolation trong SQL)

---

## License

MIT © 2026

---

<p align="center">
  <strong>Business logic as code. Metadata as data. Runtime as a platform.</strong><br>
  <em>Define once. Query forever. Ship today.</em>
</p>
