# Technology Asset Valuation Report - BMMDL Platform

> **Valuation Date:** 2026-02-11
> **Prior Assessment:** 2026-01-29
> **Methodology:** Direct codebase analysis, commit history review, architecture deep-dive
> **Commit:** fb7d837 (492 total commits)

---

## 1. Executive Summary

BMMDL (Business Meta Model Definition Language) is an enterprise-grade Domain-Specific Language platform for defining, compiling, and executing business data models. It provides a complete vertical stack — from a custom grammar and 12-pass compiler, through PostgreSQL code generation, to an OData v4-compliant runtime API and a full Vue 3 frontend — comparable in scope and ambition to SAP CDS/CAP, Mendix, or Oracle ADF.

**Key valuation drivers:**
- Proprietary DSL with 281 tokens, 100+ parser rules — highest barrier-to-entry asset
- Near-complete OData v4 implementation (25/26 features) — rare in open-source
- Native bitemporal data support — differentiator vs all comparable platforms
- 3-level multi-tenancy (Global/Tenant/Company) — built-in, not bolt-on
- Full-stack delivery: compiler + runtime + API + frontend + 12 ERP modules

---

## 2. Codebase Metrics (as of 2026-02-11)

### 2.1 Scale

| Metric | Value | Change vs Jan 29 |
|--------|-------|-------------------|
| **Total LOC** | **242,535** | +107K (+79%) |
| C# Backend | 143,543 | +7,670 |
| Frontend (Vue/TS) | 84,726 | New measurement |
| ANTLR4 Grammar | 1,338 | +500 |
| BMMDL DSL Modules | 3,441 | - |
| Sample Files | 9,987 | - |
| **Total Files** | **784** | +423 |
| C# Files | 384 | +23 |
| Frontend Files | 400 | New measurement |

### 2.2 Backend Breakdown (C#)

| Project | LOC | Files | Function |
|---------|-----|-------|----------|
| BMMDL.Compiler | 28,016 | 40 | 12-pass DSL compiler, ANTLR4 integration |
| BMMDL.Parser | 17,699 | 4 | ANTLR4 generated parser (auto-generated) |
| BMMDL.Registry | 13,395 | 47 | Meta-model persistence, EF Core, 53 DB models |
| BMMDL.Runtime.Api | 12,614 | 46 | 20 OData API controllers, deep handlers |
| BMMDL.Runtime | 11,526 | 49 | Execution engine, SQL builder, rules, permissions |
| BMMDL.CodeGen | 5,050 | 22 | PostgreSQL DDL + migration generation |
| BMMDL.Registry.Api | 2,513 | 8 | Admin module management endpoints |
| BMMDL.MetaModel | 2,501 | 11 | Core domain model (65 classes, 17 enums, zero deps) |
| BMMDL.SchemaManager | 674 | 5 | Database schema operations |
| **Core subtotal** | **93,988** | **232** | Excludes tests and generated parser |

| Test Project | LOC | Files | Test Methods |
|-------------|-----|-------|--------------|
| BMMDL.Tests | 24,961 | 77 | Primary unit + integration |
| BMMDL.Tests.New | 17,212 | 49 | E2E with Testcontainers |
| BMMDL.Tests.E2E | 7,382 | 26 | Legacy E2E |
| **Test subtotal** | **49,555** | **152** | **~1,697 test methods** |

### 2.3 Frontend Breakdown (Vue 3 + TypeScript)

| Area | LOC | Files | Key assets |
|------|-----|-------|------------|
| Components | 34,285 | 174 | Entity CRUD, admin, analytics, smart forms |
| Views | 19,930 | 51 | Entity list/detail/create/edit, admin, dashboard |
| OData Layer | 15,224 | 35 | Query builders, filter/expand parsers, metadata |
| Composables | 7,876 | 50 | useAuth, useOData, useSmartForm, useTemporal, useDelta |
| Utils | 3,084 | 19 | Validators, formatters, association display |
| Services | 1,390 | 15 | OData, metadata, admin, role, tenant, audit clients |
| Stores | 935 | 7 | Pinia state management |
| Types | 452 | 13 | TypeScript type definitions |
| **Frontend total** | **84,726** | **400** | |

### 2.4 Development Velocity

| Metric | Value |
|--------|-------|
| Total commits | 492 |
| Development period | 41 days (2026-01-02 to 2026-02-11) |
| Avg commits/day | ~12 |
| Primary contributor | phankhanhhung (447 commits, 91%) |
| AI-assisted commits | Claude (44 commits, 9%) |
| Automation | Copilot (1 commit) |

---

## 3. Technology Asset Inventory

### 3.1 DSL & Compiler (Highest-Value Asset)

**Grammar**: 1,338 LOC across BmmdlLexer.g4 (501 LOC, 281 tokens) and BmmdlParser.g4 (837 LOC, 100+ rules)

**Language features:**
- Module system with versioning, dependencies, imports/exports
- Entity definitions with inheritance, aspects (mixins), keys, defaults
- Type system: 10 primitives + localized, Array\<T\>, FileReference, custom types, enums
- Associations (4 cardinality types) with custom ON conditions
- Compositions (parent-child with implicit FK generation)
- SQL views with JOINs, GROUP BY, temporal qualifiers
- Service layer with entity exposure, actions, functions
- Access control: GRANT/DENY with scope (Global/Tenant/Company), field-level restrictions (VISIBLE/MASKED/READONLY/HIDDEN)
- Business rules: BEFORE/AFTER triggers on CRUD + ON CHANGE, validation, computed fields
- Temporal annotations: InlineHistory, SeparateTables, valid time periods
- Sequences, domain events, migrations with UP/DOWN blocks

**12-Pass Compiler Pipeline:**

| Pass | Purpose | Complexity |
|------|---------|------------|
| 1. LexicalPass | Tokenization | Standard |
| 2. SyntacticPass | Parse to AST | Standard |
| 3. ModelBuildPass | AST to BmModel domain objects | High |
| 4. SymbolResolutionPass | 4-scope symbol resolution | High |
| 5. DependencyGraphPass | Entity/type dependency graph, circular ref detection | High |
| 6. ExpressionDependencyPass | Expression-level dependency tracking | High |
| 7. BindingPass | Type binding/inference (18 expression types) | Very High |
| 8. TenantIsolationPass | Scope rule enforcement | Medium |
| 9. FileStorageValidationPass | S3/blob storage field validation | Medium |
| 10. TemporalValidationPass | Bitemporal consistency rules | High |
| 11. SemanticValidationPass | Entity/field semantic constraints | High |
| 12. OptimizationPass | Aspect inlining, deduplication | Medium |

**Expression System**: 18+ expression node types with Visitor pattern enabling: runtime evaluation, SQL generation, OData filter transformation, type inference, dependency analysis.

**Reproduction estimate**: 12-18 months for a senior compiler engineer. The combination of grammar design + multi-pass compilation + expression visitor architecture represents deep domain expertise that is extremely difficult to replicate.

### 3.2 Runtime Engine & OData v4 API

**20 API Controllers** covering the full OData v4 specification:

| Feature | Status | Notes |
|---------|--------|-------|
| CRUD (GET/POST/PATCH/DELETE) | Complete | With ETag concurrency |
| $filter | Complete | Complex expressions, temporal, full-text |
| $select | Complete | Column projection, computed fields |
| $expand | Complete | LEFT JOIN (M:1), batch sub-queries (1:N), $levels recursive |
| $orderby, $top, $skip | Complete | Multi-column, NULLS FIRST/LAST |
| $count | Complete | Collection cardinality |
| $search | Complete | Full-text across fields |
| $apply | Complete | Data aggregation (groupby, filter) |
| $compute | Complete | Computed expressions in result set |
| $value | Complete | Property-level raw value access (GET/PUT/DELETE) |
| ETag / If-Match | Complete | Weak ETags, 412 on mismatch |
| Deep Insert / Deep Update | Complete | Nested entity creation via compositions |
| $batch | Complete | JSON format with dependency tracking |
| Bound/Unbound Actions | Complete | Entity-bound + service-level operations |
| $ref | Complete | Relationship management (POST/PUT/DELETE) |
| Containment | Complete | Nested CRUD for compositions |
| Async Operations | Complete | 202 Accepted with monitoring |
| $metadata | Complete | Full CSDL with ReferentialConstraint, ContainsTarget |
| Delta Responses | Complete | $deltatoken + @odata.deltaLink |
| Singleton Entities | Complete | @OData.Singleton keyless routing |
| Capability Annotations | Complete | Filter/Sort/Expand/Insert/Update/Delete restrictions |
| Prefer Header | Complete | return=minimal, return=representation, maxpagesize |
| Computed/ReadOnly Fields | Complete | Org.OData.Core.V1.Computed + Immutable |
| Many-to-Many Associations | Complete | Junction tables + $expand through junction |
| Custom ON Conditions | Complete | Custom JOIN conditions in $expand |
| **Entity Media Streams** | **Gap** | Low priority, property-level $value covers needs |

**OData Compliance: 25/26 features (96%)** — This level of OData v4 compliance is rare even in commercial products. Microsoft's own OData libraries don't provide a full runtime — BMMDL does.

**Key Runtime Components:**

| Component | LOC | Purpose |
|-----------|-----|---------|
| DynamicSqlBuilder | 1,557 | Parameterized SQL generation (injection-safe) |
| DynamicEntityController | 2,362 | Main OData CRUD controller |
| DeepInsertHandler | ~350 | Nested entity creation with auto-FK |
| DeepUpdateHandler | ~460 | Nested update with orphan cleanup |
| RecursiveExpandHandler | ~200 | $levels recursive expansion (max depth 10) |
| RuleEngine | ~200 | BEFORE/AFTER triggers for CRUD |
| PermissionChecker | ~300 | Fail-close RBAC with scope enforcement |
| RuntimeExpressionEvaluator | ~250 | Expression AST evaluation |
| MetaModelCache | ~200 | O(1) in-memory entity/rule/ACL lookups |
| 8 OData Parsers | ~2,000 | $filter, $expand, $apply, $search, lambda, etc. |

### 3.3 Code Generation

**PostgresDdlGenerator** (1,440 LOC):
- Entity → CREATE TABLE with typed columns, PK/FK/UNIQUE constraints
- Temporal tables: auto-generates system_start/system_end, EXCLUDE constraints, history triggers
- Localized fields: auto-generates _texts companion tables
- Composition FK: auto-generates parent FK columns with CASCADE
- Junction tables: auto-generates M:M tables with alphabetical naming convention
- Array types, computed columns (STORED), CHECK constraints from rules

**MigrationScriptGenerator**: Schema diff → ALTER TABLE scripts with ADD/DROP column, constraint management, version tracking via `__bmmdl_migrations`.

### 3.4 Multi-Tenancy

3-level scope system (Global / Tenant / Company):
- Tenant context resolved from JWT claims
- Automatic WHERE filtering on tenant-scoped entities
- Compiler-time validation: prevents cross-scope references (TenantIsolationPass)
- PermissionChecker enforces scope at runtime
- Connection factory per tenant
- Schema isolation: `registry` (meta-model) vs `platform` (tenant data)

### 3.5 Temporal Data (Bitemporal)

Two storage strategies:
- **InlineHistory**: All versions in single table (system_start/system_end columns, EXCLUDE constraint for overlap prevention)
- **SeparateTables**: Main table + `_history` table with PostgreSQL triggers

Query capabilities: `asOf` (time-travel), `validAt` (business valid time), `includeHistory`, `versions all`, `versions between X and Y`, temporal predicates (OVERLAPS, CONTAINS, PRECEDES, MEETS).

**Differentiation**: Native bitemporal support is extremely rare in DSL platforms. SAP S/4HANA focuses only on transaction time. Most ORMs have zero temporal support.

### 3.6 Access Control

Multi-dimensional RBAC with:
- GRANT/DENY rules by operation (read, create, update, delete)
- Principal matching: role, user, authenticated
- Scope matching: Global, Tenant, Company
- WHERE condition evaluation on entity data
- Field-level restrictions: VISIBLE WHEN, MASKED WITH, READONLY WHEN, HIDDEN
- Fail-close policy (no rules = DENY)

### 3.7 Frontend Application

400 files, 84,726 LOC Vue 3 + TypeScript + Tailwind CSS:
- 51 view pages covering entity CRUD, admin, dashboard, audit, batch, temporal
- 174 components with SAP Fiori-style UI patterns
- 50 composables: useAuth, useOData, useSmartForm, useDraft, useInlineEdit, useBulkActions, useTemporal, useDelta, useSignalR, usePwa
- 15 API service modules
- Dynamic form generation from metadata
- Monaco editor integration for DSL editing
- Progressive Web App support

### 3.8 ERP Domain Modules

12+ domain modules with dependency chain:

| # | Module | Domain |
|---|--------|--------|
| 0 | Platform v2.0 | Identity, Tenant, Audit foundation |
| 1 | Core v1.0 | Base entities, User membership |
| 2 | MasterData v1.0 | Employee, Department, Product, Supplier |
| 3 | HR v1.0 | Leave, salary structures, payroll |
| 4 | Finance v1.0 | Payroll, financial management |
| 5 | SCM v1.0 | Sales Order, Purchase Order, Inventory |
| 6 | Rules v1.0 | Extended rules engine |
| 7 | Services v1.0 | API endpoints for all domains |
| 8 | Security v1.0 | Role, Permission, access control |
| 9 | Workflow v1.0 | Approval flows, state machine |
| 10 | Config v1.0 | System configuration |
| 11 | Warehouse v1.0 | Warehouse management (temporal) |
| - | Common | Root-level types/enums for all modules |

### 3.9 Infrastructure

docker-compose stack:

| Service | Purpose |
|---------|---------|
| PostgreSQL 16 | Primary database |
| Redis | Caching layer |
| OpenSearch 2.11 | Full-text search |
| Kafka | Event streaming |
| pgAdmin | DB admin UI |
| Kafka UI | Stream monitoring |

Plus: OpenTelemetry + Prometheus observability, Serilog structured logging, GitHub Actions CI/CD.

---

## 4. Architectural Sophistication Assessment

| Dimension | Score (1-10) | Rationale |
|-----------|:---:|-----------|
| DSL Grammar Design | 8.5 | 281 tokens, 100+ parser rules, comparable to SAP CDS |
| Compiler Architecture | 9.0 | 12-pass pipeline with dependency tracking, optimization, 4-scope resolution |
| Type System | 8.0 | 10 primitives + localized, FileReference, Array, enums, custom types |
| Runtime / SQL Builder | 9.0 | 20 controllers, parameterized SQL, 8 OData parsers, injection-safe |
| OData v4 Compliance | 9.5 | 25/26 features — near-complete spec implementation |
| Multi-tenancy | 9.0 | 3-level scope, compiler-time validation, automatic filtering |
| Temporal Data | 9.0 | Bitemporal (rare), 2 strategies, time-travel queries |
| Access Control | 8.5 | Multi-dimensional RBAC, field-level restrictions, fail-close |
| Business Rules | 8.0 | BEFORE/AFTER triggers, computed fields, expression evaluation |
| Frontend | 8.0 | 400 files, 50 composables, smart forms, enterprise UI patterns |
| ERP Modules | 7.5 | 12 domain modules, real-world coverage, dependency chain |
| Testing | 7.5 | 1,697 test methods, E2E with Testcontainers, 3 test projects |
| **Overall** | **8.5** | **Enterprise-grade** |

### Comparable Commercial Products

| Platform | Sophistication | Funding/Valuation | Notes |
|----------|:-:|---|---|
| SAP CDS/CAP | 8.5 | Part of $150B company | Closest architectural analog |
| Microsoft Dynamics 365 | 8.5 | Part of $3T company | OData-native ERP |
| Mendix | 7.5 | Acquired $730M (2018) | Low-code, less DSL depth |
| OutSystems | 7.5 | $9.5B valuation (2021) | Low-code, visual focus |
| AppGyver (SAP) | 6.5 | Acquired ~$100M (2021) | Visual low-code |
| PostgREST + Hasura | 6.5 | OSS / $100M+ funded | Auto-API, no DSL/compiler |

BMMDL has **deeper technical architecture** than most low-code platforms (compiler, temporal, OData spec compliance) while being earlier-stage in market maturity.

---

## 5. Valuation

### 5.1 Cost-to-Reproduce Method (COCOMO II)

**Backend (94K LOC core, excluding tests and generated parser):**
```
Effort = 2.94 x (KLOC)^1.0997 x EAF
       = 2.94 x (94)^1.0997 x 1.0
       = 374 person-months
```

**Frontend (85K LOC):**
```
Effort = 2.94 x (85)^1.0997 x 1.0
       = 335 person-months
```

**Combined raw effort: ~709 person-months**

| Line Item | Calculation | Value (USD) |
|-----------|-------------|-------------|
| Raw development effort | 709 PM x $15,000/month | $10,635,000 |
| DSL/Compiler premium (x1.5) | Specialized skill requirement | $15,952,500 |
| Testing/QA (30%) | 1,697 tests, 3 test projects | $4,785,750 |
| Infrastructure/DevOps (10%) | Docker, CI/CD, observability | $1,595,250 |
| Domain expertise (ERP modules) (10%) | 12 modules, real-world modeling | $1,595,250 |
| **Total Cost-to-Reproduce** | | **$24,000,000** |

**Note on AI acceleration**: The development velocity (242K LOC in 41 days) was achieved through significant AI-assisted development (9% of commits from Claude). However, cost-to-reproduce reflects what a traditional team would need, since AI tools are not guaranteed to replicate the same architectural quality.

### 5.2 Complexity-Adjusted Comparison

A traditional team attempting to reproduce BMMDL would need:

| Role | Count | Duration | Monthly Rate | Cost |
|------|-------|----------|-------------|------|
| Language/Compiler Engineer | 1 | 18 months | $18,000 | $324,000 |
| Backend Architect (.NET) | 2 | 14 months | $16,000 | $448,000 |
| OData/API Engineer | 1 | 12 months | $16,000 | $192,000 |
| Frontend Engineer (Vue) | 2 | 10 months | $14,000 | $280,000 |
| Database Engineer (PostgreSQL) | 1 | 8 months | $16,000 | $128,000 |
| QA/Test Engineer | 1 | 12 months | $12,000 | $144,000 |
| ERP Domain Consultant | 1 | 6 months | $20,000 | $120,000 |
| **Total** | **9 people** | **~18 months** | | **$1,636,000** |

**Note**: This is direct salary cost only. Loaded cost (benefits, overhead, management, tools, infrastructure) typically runs 2.5-3x salary: **$4.1M - $4.9M**.

### 5.3 Fair Market Value

Technology discount factors for pre-revenue/pre-market product:

| Factor | Impact | Reasoning |
|--------|--------|-----------|
| No production deployment | -20% | Not battle-tested at scale |
| Single primary developer | -15% | Key-person risk, limited bus factor |
| No customer/revenue traction | -15% | Market validation pending |
| Comprehensive test suite | +5% | 1,697 tests reduce integration risk |
| Modern tech stack (.NET 10) | +5% | No legacy migration needed |
| Unique differentiation (temporal + OData) | +10% | Hard to find in market |

| Scenario | Discount | CTR Base | Fair Market Value |
|----------|----------|----------|-------------------|
| Conservative | 50% | $24.0M | **$12.0M** |
| Moderate | 40% | $24.0M | **$14.4M** |
| Optimistic | 30% | $24.0M | **$16.8M** |

### 5.4 Comparison with Prior Assessment (Jan 29, 2026)

| Metric | Jan 29 | Feb 11 | Change |
|--------|--------|--------|--------|
| Total LOC (measured) | 135,873 (C# only) | 242,535 (full stack) | +79% |
| Core C# LOC | 86,000 | 94,000 | +9% |
| Frontend LOC | Not measured | 84,726 | New |
| Test methods | Not counted | 1,697 | New |
| OData features | 10 listed | 25/26 complete | +150% |
| API controllers | Not counted | 20 | New |
| Compiler passes | Not counted | 12 | New |
| ERP modules | Not counted | 12 | New |
| CTR estimate | $11.1M | $24.0M | +116% |
| FMV range | $5.5M - $7.8M | $12.0M - $16.8M | +118% |

The increase reflects: (a) previously unmeasured frontend (85K LOC), (b) significant feature additions since Jan 29 (OData features, M:M associations, access control scoping, runtime gaps), (c) more thorough accounting of complexity multipliers.

---

## 6. Value Drivers (Upside)

1. **Proprietary DSL** — 281-token grammar with 12-pass compiler is the hardest asset to replicate. Estimated 12-18 months of specialized compiler engineering.
2. **OData v4 near-completeness** — 96% spec coverage is enterprise sales-ready. SAP, Microsoft, and Oracle ecosystems all speak OData.
3. **Bitemporal data** — Native support is a genuine differentiator. Most competing platforms have zero temporal capability.
4. **3-level multi-tenancy** — Built into the compiler and runtime from day one. Not a bolt-on.
5. **Full-stack delivery** — Compiler + DDL gen + runtime + API + frontend + modules. Turnkey for buyers.
6. **Modern stack** — .NET 10, Vue 3, PostgreSQL 16. No legacy technology debt.
7. **AI development methodology** — Demonstrated ability to accelerate development 5-10x with AI pair programming.

---

## 7. Risk Factors (Downside)

1. **Key-person dependency** — 91% of commits from single developer. Knowledge transfer is a significant undertaking.
2. **No production deployment** — Technology readiness level is TRL 6-7 (system demonstrated in relevant environment). Not proven at scale.
3. **No market traction** — Zero customers, zero revenue. Market-product fit is unvalidated.
4. **Legacy parser module** — 17.7K LOC auto-generated parser is not directly valuable but inflates LOC count. (Excluded from valuation.)
5. **Test coverage depth** — 1,697 tests is substantial but may not cover all edge cases in a 94K LOC core.
6. **Operational maturity** — No runbooks, SLA definitions, incident response processes, or monitoring dashboards.

---

## 8. Strategic Value Scenarios

### Scenario A: Technology Acquisition by ERP Vendor
An ERP vendor (SAP partner, Oracle partner, Microsoft ISV) acquires BMMDL to build a next-gen low-code ERP platform.

**Value driver**: DSL + compiler + OData compliance saves 18+ months of R&D.
**Estimated value**: $14M - $17M (premium for time-to-market)

### Scenario B: SaaS Platform Launch
BMMDL is used as the foundation for a multi-tenant SaaS platform serving mid-market ERP needs.

**Value driver**: Full-stack readiness (frontend + API + modules) enables rapid go-to-market.
**Estimated value**: $8M - $12M (discounted for remaining productization work)

### Scenario C: Open-Source with Enterprise Edition
Core platform open-sourced; enterprise features (temporal, advanced access control, workflow) monetized.

**Value driver**: Community building + enterprise upsell. Comparable model: Hasura, Supabase.
**Estimated value**: Difficult to price directly; depends on community adoption velocity.

### Scenario D: Technology Licensing
License the DSL compiler + runtime to system integrators building custom ERP solutions.

**Value driver**: Per-deployment or per-seat licensing on a proven platform.
**Estimated value**: $5M - $8M (IP licensing valuation)

---

## 9. Conclusion

| Metric | Value |
|--------|-------|
| **Asset Type** | Enterprise Low-Code/DSL Platform |
| **Tech Readiness Level** | TRL 6-7 (System demonstrated in relevant environment) |
| **Total Codebase** | 242,535 LOC (784 files) across 12 C# projects + Vue frontend |
| **Architecture Score** | 8.5/10 (Enterprise-grade) |
| **OData v4 Compliance** | 96% (25/26 features) |
| **Cost-to-Reproduce** | **$24.0M** |
| **Fair Market Value Range** | **$12.0M - $16.8M** |
| **Recommended Value** | **$14.0M** |

### Key Comparable Transactions

| Company | Deal | Stage at Deal |
|---------|------|---------------|
| OutSystems (2021) | $9.5B valuation | Mature, $300M+ ARR |
| Mendix / Siemens (2018) | $730M acquisition | Mature, 4000+ customers |
| AppGyver / SAP (2021) | ~$100M acquisition | Growth, pre-revenue at scale |
| Retool (2022) | $3.2B valuation | Growth, strong community |
| Budibase (2022) | $49M seed/A | Early, open-source |

BMMDL is at a significantly earlier stage than these comparables but possesses **deeper technical architecture** (custom DSL compiler, bitemporal support, 96% OData compliance) than most. The recommended valuation of $14.0M reflects the high cost-to-reproduce with appropriate discounts for market maturity risks.

---

*This report is based on direct codebase analysis at the stated commit. It does not evaluate business model viability, market fit, IP legal status, or revenue projections. Valuation assumes clean IP ownership and transferable rights.*
