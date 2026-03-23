# BMMDL Code Review Fix Plan — V2

**Date**: 2026-03-14 (round 2, post-security-hardening)
**Review scope**: Full codebase (7 parallel agents, ~400+ files)
**Prior fixes applied**: 50 files, +765/-326 lines (commit 1dfa663)

## Issues Summary

| Severity | Count |
|----------|-------|
| CRITICAL | 20 |
| HIGH     | 26 |
| MEDIUM   | 30 |
| LOW      | 14 |
| **Total** | **90** |

---

## Work Stream Organization

Issues are organized into 12 independent work streams for parallel execution.

---

### WS1: SQL Injection in CodeGen (CRITICAL)

**Branch**: `fix/ws1-codegen-sql-injection`
**Files to modify**:
- `src/BMMDL.CodeGen/Generators/FileReferenceDdlGenerator.cs`
- `src/BMMDL.CodeGen/PostgresDdlGenerator.cs`
- `src/BMMDL.CodeGen/Schema/MigrationScriptGenerator.cs`
- `src/BMMDL.CodeGen/TypeResolver.cs`

**Issues**:
1. **[CRITICAL]** FileReference provider value SQL injection — `FileReferenceDdlGenerator.cs:60` — `fileRefType.Provider` directly interpolated into CHECK constraint. Escape with `Replace("'", "''")`
2. **[CRITICAL]** FileReference MIME type SQL injection — `FileReferenceDdlGenerator.cs:51` — AllowedMimeTypes values not escaped in IN clause. Escape each value
3. **[CRITICAL]** Sequence name SQL injection — `PostgresDdlGenerator.cs:839` — Sequence name in `nextval()` not escaped. Use QuoteIdentifier or escape
4. **[HIGH]** Migration schema name injection — `MigrationScriptGenerator.cs:312-326` — Schema names interpolated in dynamic SQL. Escape with Replace
5. **[HIGH]** Enum default value escaping — `TypeResolver.cs:364` — Enum values from `#EnumVal` not escaped before quoting
6. **[MEDIUM]** Unquoted index column names — `PostgresDdlGenerator.cs:593` — Column names in INDEX DDL not quoted via QuoteIdentifier
7. **[HIGH]** Empty index columns array access — `PostgresDdlGenerator.cs:593` — No bounds check on `fi.Columns[0]`
8. **[MEDIUM]** Inconsistent identifier quoting — Table/column names sometimes quoted, sometimes not

**Acceptance**: All string values interpolated into DDL must be escaped or quoted. No raw user-derived values in SQL.

---

### WS2: Tenant Isolation Gaps (CRITICAL)

**Branch**: `fix/ws2-tenant-isolation`
**Files to modify**:
- `src/BMMDL.Runtime/Collaboration/Comment.cs`
- `src/BMMDL.Runtime/Reports/ReportTemplate.cs`
- `src/BMMDL.Runtime/Events/WebhookConfig.cs`
- `src/BMMDL.Runtime.Api/Controllers/CollaborationController.cs`
- `src/BMMDL.Runtime.Api/Controllers/ReportController.cs`
- `src/BMMDL.Runtime.Api/Middleware/TenantContextMiddleware.cs`

**Issues**:
1. **[CRITICAL]** CommentStore/ChangeRequestStore missing tenant_id — All queries use only `record_key` without tenant filtering. Add tenant_id column and filter all queries
2. **[CRITICAL]** ReportStore missing tenant_id — Report templates accessible across tenants. Add tenant_id to all queries
3. **[CRITICAL]** TenantContextMiddleware allows unauthenticated tenant override — If userContext is null, X-Tenant-Id header is accepted. Fail if header present but not authenticated
4. **[HIGH]** WebhookStore global visibility — `GetAllAsync()` returns all webhooks without tenant filter
5. **[HIGH]** EntityNavigationController missing cross-tenant check — Both parent/child entities not verified to belong to same tenant
6. **[MEDIUM]** Temporal queries may leak cross-tenant history — Verify TemporalQueryBuilder applies tenant_id to UNION subqueries
7. **[MEDIUM]** CollaborationController recordKey not validated against tenant — Craft recordKey for entities in other tenants

**Acceptance**: Every data store query must include tenant_id in WHERE clause. No cross-tenant data access possible.

---

### WS3: Auth Bypass & Test Tokens (CRITICAL)

**Branch**: `fix/ws3-auth-bypass`
**Files to modify**:
- `src/BMMDL.Runtime.Api/Services/OAuthOptions.cs`
- `src/BMMDL.Runtime.Api/Services/OAuthValidatorService.cs`
- `src/BMMDL.Registry.Api/Authorization/AdminKeyAuthorizationHandler.cs`
- `src/BMMDL.Runtime.Api/Program.cs`
- `src/BMMDL.Registry.Api/Program.cs`
- `src/BMMDL.Runtime.Api/appsettings.json`

**Issues**:
1. **[CRITICAL]** AllowTestTokens defaults true — `OAuthOptions.cs:18` — Any `TEST_*` token bypasses auth in production. Default to false, add production guard
2. **[CRITICAL]** AdminKeyAuthorizationHandler missing context.Fail() — `AdminKeyAuthorizationHandler.cs:32-73` — Returns Task.CompletedTask without Fail() on invalid key. Add explicit context.Fail()
3. **[HIGH]** TEST_ token parsing insufficient — `OAuthValidatorService.cs:74-88` — Only requires 3 parts, no format validation. Only accept in Development
4. **[HIGH]** Admin auth bypass on empty key — If Admin:ApiKey is empty, handler silently allows access. Throw on empty config
5. **[MEDIUM]** Production secret validation incomplete — Missing JWT length check (>= 32 chars), AllowTestTokens check, Issuer/Audience check
6. **[MEDIUM]** Default admin key in appsettings.Development.json — Should fail loudly in production, not just warn

**Acceptance**: AllowTestTokens=false in production. AdminKeyHandler calls context.Fail() on all rejection paths. No TEST_ tokens accepted outside Development.

---

### WS4: Plugin State Machine Ordering (CRITICAL)

**Branch**: `fix/ws4-plugin-state-machine`
**Files to modify**:
- `src/BMMDL.Runtime/Plugins/PluginManager.cs`

**Issues**:
1. **[CRITICAL]** EnablePluginAsync — DB status updated BEFORE OnEnabledAsync hook. If hook fails, DB shows enabled but plugin not initialized. Move DB update after hook
2. **[CRITICAL]** DisablePluginAsync — Same bug. DB updated BEFORE OnDisabledAsync. Move after hook
3. **[CRITICAL]** ActivateGlobalFeatureIfSupported called after cache update — If it throws, cache is stale. Call before cache update
4. **[HIGH]** InstallPluginAsync — State row inserted BEFORE module installation. Move module install before state persist
5. **[HIGH]** UninstallPluginAsync — OnUninstalledAsync runs BEFORE down-migrations. Run migrations first
6. **[HIGH]** Global features not re-activated on restart — EnablePluginAsync doesn't call ActivateGlobalFeatureIfSupported for already-enabled plugins
7. **[HIGH]** No transaction wrapping install sequence — Migrations + hook + state insert should be one transaction
8. **[MEDIUM]** IsPluginEnabled sync cache doesn't sync with async DB — Race window after enable
9. **[LOW]** Bootstrap doesn't fail-fast for critical plugins — MultiTenancyPlugin failure is logged but startup continues

**Acceptance**: All lifecycle hooks execute BEFORE state persistence. Global features reactivated on restart. Install is atomic.

---

### WS5: Runtime AsyncLocal & Concurrency (CRITICAL)

**Branch**: `fix/ws5-runtime-concurrency`
**Files to modify**:
- `src/BMMDL.Runtime/Expressions/RuntimeExpressionEvaluator.cs`
- `src/BMMDL.Runtime/Rules/StatementExecutor.cs`
- `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs`
- `src/BMMDL.Runtime/DataAccess/ParameterizedQueryExecutor.cs`
- `src/BMMDL.Runtime/DataAccess/QueryPlanCache.cs`
- `src/BMMDL.Runtime/MetaModelCacheManager.cs`
- `src/BMMDL.Runtime/Authorization/PermissionChecker.cs`

**Issues**:
1. **[CRITICAL]** AsyncLocal context leak in RuntimeExpressionEvaluator — `_context.Value` never cleared. Wrap in try-finally
2. **[CRITICAL]** AsyncLocal snapshot bug in StatementExecutor — Shallow copy risk. Ensure proper snapshot/restore
3. **[HIGH]** Parameter reindexing regex — `DynamicSqlBuilder.cs:733` — Regex can match parameter names inside string literals. Use proper parsing
4. **[HIGH]** ToPascalCase mismatch — `RuntimeExpressionEvaluator.cs:784` — Local reimplementation differs from NamingConvention.ToPascalCase(). Use the standard one
5. **[HIGH]** Regex parsing in PatchInsertNulls — `ParameterizedQueryExecutor.cs:244` — Doesn't handle quoted identifiers with parentheses
6. **[HIGH]** QueryPlanCache parameter cloning missing NpgsqlDbType — Clone full parameter including type
7. **[HIGH]** SemaphoreSlim never disposed in MetaModelCacheManager — Implement IDisposable
8. **[MEDIUM]** PermissionChecker ExtendsFrom loop incomplete — Only follows one parent chain. Use queue/stack for BFS
9. **[MEDIUM]** Division by zero returns null silently — Should throw InvalidOperationException
10. **[MEDIUM]** ConvertArrayValue JSON parse without context — Wrap and throw ArgumentException with field name

**Acceptance**: AsyncLocal values cleared in finally blocks. All parameter operations use standard naming. SemaphoreSlim disposed.

---

### WS6: API Controller Security (CRITICAL)

**Branch**: `fix/ws6-api-controller-security`
**Files to modify**:
- `src/BMMDL.Runtime.Api/Controllers/EntityActionController.cs`
- `src/BMMDL.Runtime.Api/Controllers/BatchController.cs`
- `src/BMMDL.Runtime.Api/Hubs/NotificationHub.cs`
- `src/BMMDL.Runtime.Api/Controllers/AuthController.cs`

**Issues**:
1. **[CRITICAL]** SQL injection in M:M junction query — `EntityActionController.cs:403` — FK column names not parameterized. Use QuoteIdentifier
2. **[CRITICAL]** NotificationHub record lock TOCTOU race — ConcurrentDictionary check-then-act not atomic. Use AddOrUpdate
3. **[HIGH]** Batch transaction handling — One atomic group failure rolls back ALL groups. Use per-group savepoints
4. **[HIGH]** Unparameterized LIMIT/OFFSET in M:M query — `EntityActionController.cs:438` — Integer interpolation. Use parameters
5. **[HIGH]** Auth register tenant inconsistency — `AuthController.cs:89` — TenantId=Guid.Empty on register, different on login
6. **[MEDIUM]** Batch exception message leakage — `BatchController.cs:313` — Catches exception but returns ex.Message. Use generic message for DB errors
7. **[MEDIUM]** Missing rate limiting on plugin admin endpoints — DoS via repeated scan/load/upload
8. **[MEDIUM]** SignalR lock release race — Use atomic TryRemove before broadcast

**Acceptance**: All SQL uses parameterized values or QuoteIdentifier. Lock operations are atomic. Batch transactions per-group.

---

### WS7: Plugin Loader & Registry (HIGH)

**Branch**: `fix/ws7-plugin-loader`
**Files to modify**:
- `src/BMMDL.Runtime/Plugins/Loading/PluginDirectoryLoader.cs`
- `src/BMMDL.Runtime/Plugins/PlatformFeatureRegistry.cs`

**Issues**:
1. **[HIGH]** RebuildRegistry called outside lock — `PluginDirectoryLoader.cs:774` — Registry rebuild after releasing lock. Call inside lock
2. **[HIGH]** File watcher errors silently swallowed — `PluginDirectoryLoader.cs:990-1068` — Log at Error level, ensure all exceptions are caught
3. **[HIGH]** ExtractZipToDirectory partial cleanup — If load fails after extraction, directory not cleaned up. Add try-finally
4. **[MEDIUM]** ZipSlip case-sensitive path comparison — Uses OrdinalIgnoreCase on Linux. Match OS default
5. **[MEDIUM]** AssemblyLoadContext not disposed on feature discovery failure — Use try-finally for cleanup
6. **[MEDIUM]** ValidateNoDependentsEnabledAsync race condition — Check at DB level for atomicity

**Acceptance**: Registry rebuild inside lock. Zip extraction cleaned up on failure. AssemblyLoadContext properly disposed on error.

---

### WS8: Frontend Security (HIGH)

**Branch**: `fix/ws8-frontend-security`
**Files to modify**:
- `frontend/src/composables/useAuth.ts`
- `frontend/src/composables/useOAuth.ts`
- `frontend/src/services/api.ts`
- `frontend/src/utils/odataQueryBuilder.ts`
- `frontend/src/utils/preferences.ts`
- `frontend/src/odata/DraftManager.ts`

**Issues**:
1. **[CRITICAL]** Open redirect bypass via URL encoding — `useAuth.ts:8-11` — `/%2F%2Fevil.com` bypasses check. Use `new URL()` for validation
2. **[CRITICAL]** Unvalidated token refresh response — `api.ts:167` — Blindly stores response tokens. Validate JWT structure
3. **[HIGH]** Token refresh race condition — `api.ts:118-192` — Multiple concurrent 401s trigger multiple refreshes. Use Promise memoization
4. **[HIGH]** Untrusted tenant ID from localStorage — `api.ts:74-98` — No UUID format validation. Add regex check
5. **[HIGH]** Draft data exposure in localStorage — `DraftManager.ts:331` — Sensitive entity data in plaintext. Use sessionStorage for data
6. **[HIGH]** localStorage preferences deserialization — `preferences.ts:28` — No type validation on parsed objects
7. **[MEDIUM]** OData field name injection — `odataQueryBuilder.ts:41` — Field names not validated as identifiers
8. **[MEDIUM]** OAuth nonce not validated — `useOAuth.ts:45` — nonce created but never checked in id_token
9. **[MEDIUM]** Error messages from auth endpoint shown verbatim — Return generic messages to UI

**Acceptance**: Open redirect fully blocked. Token refresh uses single promise. localStorage values validated. Draft data in sessionStorage.

---

### WS9: Registry API Hardening (HIGH)

**Branch**: `fix/ws9-registry-hardening`
**Files to modify**:
- `src/BMMDL.Registry.Api/Authorization/AdminKeyAuthorizationHandler.cs`
- `src/BMMDL.Registry.Api/Controllers/AdminController.cs`
- `src/BMMDL.Registry.Api/Services/AdminService.cs`
- `src/BMMDL.Registry.Api/Program.cs`

**Issues**:
1. **[CRITICAL]** AdminKeyAuthorizationHandler missing context.Fail() — Returns without failure signal on invalid key (duplicate of WS3 #2, fix here)
2. **[HIGH]** Health endpoint exposes admin endpoints — `AdminController.cs:195-240` — Remove endpoint list or require auth
3. **[MEDIUM]** UninstallModule no input validation — `AdminController.cs:154` — No null/whitespace check on moduleName
4. **[MEDIUM]** ClearDatabase no schema whitelist — `AdminService.cs:60` — Can attempt to drop pg_catalog, public. Add whitelist
5. **[MEDIUM]** Default password in connection string fallback — `Program.cs:29` — Add to production validation
6. **[LOW]** Compilation file read race — `ModuleCompilationService.cs:620` — Wrap in try-catch for FileNotFoundException

**Acceptance**: Authorization handler explicitly fails. Health endpoint doesn't leak endpoints. Schema drops are whitelisted.

---

### WS10: Security Headers & CSRF (MEDIUM)

**Branch**: `fix/ws10-security-headers`
**Files to modify**:
- `src/BMMDL.Runtime.Api/Middleware/SecurityHeadersMiddleware.cs`
- `src/BMMDL.Runtime.Api/Program.cs`

**Issues**:
1. **[MEDIUM]** Missing Content-Security-Policy header — Add `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'`
2. **[MEDIUM]** CORS AllowAnyOrigin with AllowCredentials (already fixed in WS2 from prior round — verify)
3. **[LOW]** Missing CSRF token validation — API is JWT-based (CSRF-resistant), but form-based login could use anti-forgery
4. **[LOW]** Docker user permissions — Minor hardening for container image

**Acceptance**: CSP header added. CORS verified fixed. CSRF not critical for JWT API.

---

### WS11: Test Coverage Gaps (HIGH)

**Branch**: `fix/ws11-test-coverage`
**Files to modify**:
- `src/BMMDL.Tests/` — new test files
- `src/BMMDL.Tests.New/Compiler/ModuleCompilationTests.cs`

**Issues**:
1. **[HIGH]** Auth bypass tests skipped — `ModuleCompilationTests.cs:265-282` — Unskip and implement
2. **[HIGH]** ClearDatabase authorization not tested — Add test for unauthorized access
3. **[HIGH]** Bootstrap authorization not tested — Add test for unauthorized access
4. **[MEDIUM]** Module uninstall dependency check test skipped — Implement the test
5. **[MEDIUM]** Invalid BMMDL compilation edge cases — Missing tests for empty source, syntax errors, circular deps
6. **[LOW]** Token expiry enforcement test — Verify expired refresh tokens are rejected

**Acceptance**: All skipped security tests unskipped and passing. Auth bypass scenarios covered.

---

### WS12: Data Access & SQL Safety (MEDIUM)

**Branch**: `fix/ws12-data-access-safety`
**Files to modify**:
- `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs`
- `src/BMMDL.Runtime/DataAccess/ParameterizedQueryExecutor.cs`

**Issues**:
1. **[MEDIUM]** String-based WHERE clause field lookup — `DynamicSqlBuilder.cs:393` — `IndexOf("is_deleted = false")` fragile. Use structured clauses
2. **[MEDIUM]** Unqualified column references in inheritance queries — `DynamicSqlBuilder.cs:403` — String prepend of "p." brittle
3. **[MEDIUM]** Missing empty key fields validation — `PostgresDdlGenerator.cs:517` — Entity with no key generates invalid PRIMARY KEY()
4. **[LOW]** Temporal update errors logged at Debug — Should be Warn or Error in production
5. **[LOW]** String literal regex in migration — `MigrationScriptGenerator.cs:570` — Doesn't handle PostgreSQL doubled-quote escaping

**Acceptance**: WHERE clause handling uses structured objects where possible. Empty key fields validated.

---

## Execution Plan

### Phase 1: CRITICAL Security (WS1-WS6)
All 6 streams can run in parallel. These address SQL injection, auth bypass, tenant isolation, and state machine bugs.

### Phase 2: HIGH Priority (WS7-WS9)
Plugin loader, frontend security, and registry hardening. Run in parallel after Phase 1.

### Phase 3: MEDIUM Priority (WS10-WS12)
Security headers, test coverage, and data access safety. Run in parallel after Phase 2.

---

## Deduplication Notes

Some issues appear in multiple agent reports. Canonical locations:
- AdminKeyAuthorizationHandler fix → WS3 (auth) + WS9 (registry). Fix in one, verify in both.
- AllowTestTokens → WS3
- CORS fix → Already done in prior round, verify in WS10
- CSP header → WS10
- Tenant isolation → WS2 (single stream for all stores)
