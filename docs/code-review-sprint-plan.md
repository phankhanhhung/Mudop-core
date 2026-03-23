# BMMDL Code Review — Sprint Plan

**Review Date**: 2026-03-14
**Total Issues Found**: 162 (33 Critical, 43 High, 51 Medium, 35 Low)

---

## Sprint 1 — Security Hardening (Critical)

| # | ID | Issue | Location | Status |
|---|-----|-------|----------|--------|
| 1 | S2 | Frontend cache missing tenant isolation | `frontend/src/services/odataService.ts` | DONE |
| 2 | S5 | Registry API tenant ID from query param bypass | `src/BMMDL.Registry.Api/Controllers/RegistryControllers.cs` | DONE |
| 3 | S7 | Registry stats queries missing tenant filter | `src/BMMDL.Registry/Repositories/EfCoreMetaModelRepository.cs` | DONE |
| 4 | S1 | JWT token moved to sessionStorage | `frontend/src/services/api.ts` | DONE |
| 5 | S3 | CSRF X-Requested-With header added | Frontend | DONE |
| 6 | D1 | Unquoted table names in DROP scripts | `src/BMMDL.SchemaManager/PostgresSchemaManager.cs` | DONE |
| 7 | D2 | Nullable logic inversion in TypeResolver (6 locations) | `src/BMMDL.CodeGen/TypeResolver.cs` | DONE |
| 8 | S6 | Batch controller per-request auth centralized | `src/BMMDL.Runtime.Api/Controllers/BatchController.cs` | DONE |
| 9 | FE-ETag | ETag storage tenant isolation added | `frontend/src/services/odataService.ts` | DONE |

---

## Sprint 2 — Data Integrity & Concurrency

| # | ID | Issue | Location | Status |
|---|-----|-------|----------|--------|
| 1 | D4 | EF Core xmin concurrency tokens (4 entities) | `src/BMMDL.Registry/Data/RegistryDbContext.cs` | DONE |
| 2 | R2 | SQL param names prefixed per-section (8 builders) | `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` | DONE |
| 3 | R1 | Interlocked.Add for TotalTokens | `src/BMMDL.Compiler/Pipeline/Passes/LexicalPass.cs` | DONE |
| 4 | R3 | SemaphoreSlim lifecycle lock (4 ops + bootstrap) | `src/BMMDL.Runtime/Plugins/PluginManager.cs` | DONE |
| 5 | R4 | ALC creation moved inside lock | `src/BMMDL.Runtime/Plugins/Loading/PluginDirectoryLoader.cs` | DONE |
| 6 | D5 | Temporal pre-exec validation + case-insensitive patch | `src/BMMDL.Runtime/DataAccess/ParameterizedQueryExecutor.cs` | DONE |
| 7 | R5 | NamespaceCache → ConcurrentDictionary | `src/BMMDL.Registry/Repositories/Persistence/RepositoryContext.cs` | DONE |
| 8 | D3 | MetaModelCache keep-first + warn on duplicates (8 indexes) | `src/BMMDL.Runtime/MetaModelCache.cs` | DONE |

---

## Sprint 3 — Reliability & Quality

| # | ID | Issue | Location | Status |
|---|-----|-------|----------|--------|
| 1 | Q1 | Diagnostics added to 7 catch blocks (3 files) | `BmExpressionBuilder.cs` + 2 more | DONE |
| 2 | Q2 | 5 skip rules + 130+ SQL keywords | `MigrationScriptGenerator.cs` | DONE |
| 3 | Q3 | Dedup refCount decrement + conditional delete | `frontend/src/utils/requestDedup.ts` | DONE |
| 4 | — | Rate limiting: register (5/15m) + refresh (30/15m) | `AuthController.cs` | DONE |
| 5 | — | Plugin code signing for external DLLs | `PluginDirectoryLoader.cs` | SKIPPED (needs PKI infra) |
| 6 | — | Tenant validation on all 4 DML ops + 4 tests | `DmlBuilder.cs` | DONE |
| 7 | — | OnInstalledAsync + state INSERT in single txn (9 files) | `PluginManager.cs` + plugins | DONE |
| 8 | — | Exponential backoff (5 retries) + 30s timeout | `PluginDirectoryLoader.cs` | DONE |

---

## Sprint 4 — Performance & Polish

| # | ID | Issue | Location | Status |
|---|-----|-------|----------|--------|
| 1 | — | CSDL cached + ETag + 304 Not Modified | `ODataMetadataController.cs` + `MetaModelCacheManager.cs` | DONE |
| 2 | — | CallTargetResolver O(1) indexed lookup (4 dicts) | `CallTargetResolver.cs` | DONE |
| 3 | — | GetDependentModulesAsync server-side + 10x AsNoTracking | `Repositories.cs` | DONE |
| 4 | — | Pagination validation on 6 controllers | `DynamicEntityController.cs` + 5 more | DONE |
| 5 | — | autoLoad .catch() in useMetadata + useOData | `frontend/src/composables/` | DONE |
| 6 | — | Aspect field conflict warnings with ILogger | `PostgresDdlGenerator.cs` | DONE |
| 7 | — | Pipeline exception: inner ex + full stack trace via _output | `CompilerPipeline.cs` | DONE |
| 8 | — | AsNoTracking already applied everywhere | `EfCoreMetaModelRepository.cs` | N/A (already done) |

---

## Round 2 — Security Hardening (Post-Sprint Review)

**Review Date**: 2026-03-15
**Issues Found**: 14 new issues + 6 optimizations from second full-codebase review

| # | ID | Issue | Location | Status |
|---|-----|-------|----------|--------|
| 1 | N1 | Regex `$` injection in VALUES patching | `ParameterizedQueryExecutor.cs` | DONE |
| 2 | N2 | UserPreference tenant validation (Update/Delete/SetDefault) | `UserPreferenceController.cs` | DONE |
| 3 | N3 | ReportController GetReportData missing auth | `ReportController.cs` | DONE |
| 4 | N4 | CollaborationController cross-tenant entity access | `CollaborationController.cs` | DONE |
| 5 | N5 | Batch error message leaks raw exceptions | `BatchController.cs` | DONE |
| 6 | N6 | Legacy `/api/v1` route tenant bypass | `TenantContextMiddleware.cs` | DONE |
| 7 | N7 | Temporal UPDATE missing RepeatableRead isolation | `ParameterizedQueryExecutor.cs` | DONE |
| 8 | N8 | CORS AllowAny* → explicit methods/headers | `Program.cs` | DONE |
| 9 | N9 | ExceptionMiddleware SQL log sanitization | `ExceptionMiddleware.cs` | DONE |
| 10 | N10 | Missing DB indexes (3 tables) | `RegistryDbContext.cs` | DONE |
| 11 | N11 | EntityListView guard flags → reactive ref() | `EntityListView.vue` | DONE |
| 12 | N12 | StatementExecutor silent swallow in fail-fast mode | `StatementExecutor.cs` | DONE |
| 13 | N13 | FilterExpressionParser off-by-one | `FilterExpressionParser.cs` | DONE |
| 14 | N14 | Dead code removal (OverrideAuditTimestamp + stale comment) | `DmlBuilder.cs`, `DynamicSqlBuilder.cs` | DONE |
| 15 | — | CallTargetResolver O(1) indexed lookup | `CallTargetResolver.cs` | DONE |
| 16 | — | MetaModelCacheManager version counter | `MetaModelCacheManager.cs` | DONE |
| 17 | — | CSDL $metadata SHA256 ETag + 304 Not Modified | `ODataMetadataController.cs` | DONE |
| 18 | — | PostgresDdlGenerator ILogger + aspect conflict warning | `PostgresDdlGenerator.cs` | DONE |
| 19 | — | AsNoTracking on 10 read-only queries + server-side deps | `Repositories.cs` | DONE |
| 20 | — | Pagination validation on 6 controllers | 6 controllers | DONE |

---

## Theme Analysis

### Multi-Tenancy Isolation (8 issues, Sprints 1-2)
Cross-cutting concern affecting Frontend cache, Registry API, Stats, ETag, Batch, DML builders, AuthorizationMiddleware, Metadata endpoint.

### SQL Generation Safety (6 issues, Sprints 1-3)
Unquoted identifiers, feature PostTableStatements, regex identifier conversion, parameter collisions, column quoting, view DDL.

### Silent Failures (7 issues, Sprints 2-3)
Empty catch blocks, duplicate overwrites, aspect conflicts, unknown aspects, metadata loading, plugin features, null returns.

### Frontend Security (5 issues, Sprint 1)
localStorage tokens, CSRF, CSP headers, redirect validation, OData injection.
