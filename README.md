# Mudop (BMMDL)

A meta-model-driven platform that compiles business domain definitions into PostgreSQL schemas, OData v4 REST APIs, and enterprise runtime engines — with multi-tenancy, bitemporal data, and zero-downtime evolution built in.

## DSL Example

```bmmdl
entity Order {
    key ID: UUID;
    customer: Association to Customer;
    totalAmount: Decimal computed subtotal + taxAmount;
    items: Composition of OrderItem;
}

rule OrderValidation for Order {
    on before CREATE, UPDATE {
        validate totalAmount > 0 message 'Total must be positive';
    }
}

access control for Order {
    grant READ to role 'User' at TENANT scope;
    grant UPDATE to role 'Manager' where department.managerId = $current_user.id;
}
```

From this definition, Mudop generates: database schema with RLS and triggers, OData v4 API with full query support, business rule enforcement on every CRUD operation, and role-based authorization with field-level masking.

## Quick Start

```bash
# Build & test
dotnet build BMMDL.sln
dotnet test

# Start PostgreSQL
docker run -d --name mudop-postgres -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:17

# Start APIs
dotnet run --project src/BMMDL.Registry.Api   # Control Plane (port 5002)
dotnet run --project src/BMMDL.Runtime.Api     # Data Plane (port 5000)

# Frontend
cd frontend && npm install && npm run dev      # Port 5173

# Bootstrap & authenticate
curl -X POST http://localhost:5002/api/admin/bootstrap
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "admin123"}'
```

Or with Docker:

```bash
docker build -t mudop .
docker run -p 5000:5000 mudop
```

## Project Structure

```
src/
  BMMDL.MetaModel/       Core domain types (zero dependencies)
  BMMDL.Compiler/        12-pass compiler + CLI
  BMMDL.CodeGen/         PostgreSQL DDL generation
  BMMDL.Runtime/         Rule engine, expression evaluator, auth, events
  BMMDL.Runtime.Api/     OData v4 REST API (data plane)
  BMMDL.Registry/        Module versioning, persistence
  BMMDL.Registry.Api/    Admin REST API (control plane)
  BMMDL.Tests/           Unit tests
  BMMDL.Tests.New/       E2E tests (12 test categories)
Grammar/                 ANTLR4 grammar definitions
erp_modules/             12-module ERP library (50+ entities)
frontend/                Vue 3 + TypeScript + Pinia + Tailwind CSS
```

## Key Features

**OData v4 Runtime** — Full query support: `$filter`, `$expand`, `$select`, `$orderby`, `$apply`, `$search`, `$compute`. Deep insert/update, `$batch` with dependency tracking, `$ref` relationship management, ETag concurrency, `$deltatoken` change tracking, async operations (202 Accepted), `$value` property access, singletons, and capability annotations.

**Multi-Tenancy** — First-class language support. Compiler auto-injects tenant isolation (RLS policies, composite FKs, scoped indexes). Use `@GlobalScoped` to opt out for reference data.

**Bitemporal Data** — `@Temporal` annotation enables time-travel queries (`asOf`, `validAt`, `includeHistory`). Supports inline history and separate history tables.

**Authorization** — Declarative RBAC with row-level security, field-level restrictions (hidden/masked), fail-close policy. Dual-mode auth: JWT for users, `X-Admin-Key` for admin operations. Google OAuth supported.

**Business Rules & Events** — Before/after triggers for CRUD with validate, compute, and conditional logic. 40+ built-in expression functions. Domain event publishing (EntityCreated/Updated/Deleted) with auto audit logging.

**Schema Evolution** — Zero-downtime upgrades via dual-version sync with identity preservation across versions.

**Views & Sequences** — SQL-like projections with materialized view auto-refresh, temporal view support, role-based access. Tenant-scoped auto-numbering sequences with configurable formats.

**Observability** — OpenTelemetry integration, application metrics, structured logging, health probes.

## Frontend

Vue 3 SPA with TypeScript, Pinia state management, and Tailwind CSS:

- Entity CRUD (list, detail, create, edit) with composition support
- Admin module management (compile/install)
- Multi-tenant management (create, switch)
- Google OAuth + JWT login/register
- Association pickers, enum fields, field-level masking display

## Compiler Pipeline

12 passes: Lexical -> Syntactic -> Model Build -> Symbol Resolution -> Dependency Graph -> Expression Dependency -> Binding & Type Inference -> Tenant Isolation -> File Storage Validation -> Temporal Validation -> Semantic Validation -> Optimization.

## Tech Stack

- .NET 10, PostgreSQL 15+, ANTLR4
- Vue 3, TypeScript, Pinia, Tailwind CSS
- xUnit, FluentAssertions, Testcontainers
- Docker, GitHub Actions CI

## Documentation

- [Architecture](docs/ARCHITECTURE.md) — System design and compiler pipeline
- [Development](docs/DEVELOPMENT.md) — Environment setup and workflows
- [Multi-Tenancy](docs/TENANT_FIRST_CLASS.md) — Tenant isolation guide
- [Roadmap](docs/ROADMAP.md) — Status and future plans

## License

MIT — See [LICENSE](LICENSE) for details.
