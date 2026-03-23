# BMMDL Code Review Fix Plan

**Date**: 2026-03-14
**Review scope**: Full codebase (7 parallel agents, ~300+ files)
**Total issues found**: 111 (32 CRITICAL, 37 HIGH, 34 MEDIUM, 8 LOW)

---

## Overview

This plan addresses all CRITICAL and HIGH issues from the comprehensive code review.
Work is organized into 10 independent work streams that can execute in parallel.

Each work stream has:
- A clear scope (files to modify)
- No dependency on other work streams
- Estimated test count
- Acceptance criteria

---

## Work Stream 1: Secrets & Configuration Hardening

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Small

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| S1 | CRITICAL | Hardcoded DB credentials in appsettings.json |
| S2 | CRITICAL | Hardcoded JWT secret in appsettings.json |
| S3 | CRITICAL | Hardcoded S3 keys in appsettings.Development.json |
| S4 | HIGH | Admin API key fallback to well-known default |
| S5 | HIGH | Registry API credential fallback to hardcoded values |

### Files to Modify
- `src/BMMDL.Runtime.Api/appsettings.json`
- `src/BMMDL.Runtime.Api/appsettings.Development.json`
- `src/BMMDL.Registry.Api/appsettings.json`
- `src/BMMDL.Runtime.Api/Program.cs` (add startup validation)
- `src/BMMDL.Registry.Api/Program.cs` (add startup validation)

### Tasks
1. Replace all hardcoded credentials in appsettings.json with placeholder values that cause clear errors
2. Keep hardcoded dev credentials ONLY in appsettings.Development.json (not base appsettings)
3. Add startup validation in both Program.cs files: if `!app.Environment.IsDevelopment()` and secrets are at default values, throw `InvalidOperationException` with clear message
4. Validate: JWT SecretKey, Admin:ApiKey, ConnectionStrings, S3 AccessKey/SecretKey
5. Remove credential fallback chains in Registry API Program.cs — require explicit config in production

### Acceptance Criteria
- [ ] No hardcoded credentials in base appsettings.json files
- [ ] Application throws on startup in non-Development if secrets are defaults
- [ ] Development environment still works without any config changes
- [ ] All existing tests pass

---

## Work Stream 2: HTTP Security Infrastructure

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Medium

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| H1 | CRITICAL | Missing HTTPS enforcement in production |
| H2 | CRITICAL | SQL statements leaked in error responses |
| H3 | HIGH | Missing security headers (X-Frame-Options, CSP, X-Content-Type-Options) |
| H4 | HIGH | Overly permissive CORS in development |
| H5 | HIGH | Sensitive data (UserIds, SQL) logged in AuthorizationMiddleware |
| H6 | HIGH | Health check endpoints expose sensitive info |

### Files to Modify
- `src/BMMDL.Runtime.Api/Program.cs` (HTTPS, CORS, middleware ordering)
- `src/BMMDL.Registry.Api/Program.cs` (HTTPS, CORS)
- `src/BMMDL.Runtime.Api/Middleware/ExceptionMiddleware.cs`
- `src/BMMDL.Runtime.Api/Middleware/AuthorizationMiddleware.cs`
- `src/BMMDL.Runtime.Api/Controllers/HealthController.cs`
- NEW: `src/BMMDL.Runtime.Api/Middleware/SecurityHeadersMiddleware.cs`

### Tasks
1. **ExceptionMiddleware**: Remove SQL from error responses — never include `pe.Data["FailingSQL"]` or `pe.MessageText` in client response. Map PostgresException to generic error codes. Log full details server-side only
2. **SecurityHeadersMiddleware** (new): Add `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `X-XSS-Protection: 1; mode=block`, `Referrer-Policy: strict-origin-when-cross-origin`
3. **Program.cs**: Add `app.UseHsts()` and `app.UseHttpsRedirection()` for non-Development
4. **CORS**: In development, restrict to `http://localhost:5173` and `http://localhost:3000` instead of `AllowAnyOrigin()`. In production, require `Cors:AllowedOrigins` config or throw
5. **AuthorizationMiddleware**: Remove UserId/TenantId from log messages. Log only module/entity context. Never log SQL queries with parameter values
6. **HealthController**: Remove exception messages from health response. Return only `healthy`/`unhealthy` status without details. Keep `/health/live` public but add `[Authorize]` to `/health/ready`

### Acceptance Criteria
- [ ] No SQL fragments in any error response (verify with integration test)
- [ ] Security headers present on all responses
- [ ] CORS restricted to specific origins even in development
- [ ] Health endpoint does not expose exception details
- [ ] All existing tests pass

---

## Work Stream 3: Authentication & Authorization Gaps

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Medium

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| A1 | CRITICAL | $metadata endpoint unauthenticated — full schema disclosure |
| A2 | CRITICAL | SignalR NotificationHub missing [Authorize] |
| A3 | HIGH | Plugin manifest endpoint [AllowAnonymous] |
| A4 | HIGH | Tenant isolation bypass via X-Tenant-Id header override |
| A5 | HIGH | Batch operations skip per-entity permission checks |
| A6 | MEDIUM | Missing rate limiting on auth endpoints |
| A7 | MEDIUM | Actions only check Execute permission, not nested CRUD |

### Files to Modify
- `src/BMMDL.Runtime.Api/Controllers/ODataMetadataController.cs`
- `src/BMMDL.Runtime.Api/Hubs/NotificationHub.cs`
- `src/BMMDL.Runtime.Api/Controllers/PluginController.cs`
- `src/BMMDL.Runtime.Api/Middleware/TenantContextMiddleware.cs`
- `src/BMMDL.Runtime.Api/Controllers/BatchController.cs`
- `src/BMMDL.Runtime.Api/Controllers/AuthController.cs`

### Tasks
1. **ODataMetadataController**: Add `[Authorize]` to class. Keep the public share report endpoint `[AllowAnonymous]` only where explicitly needed
2. **NotificationHub**: Add `[Authorize]` to class. Validate `Context.User.Identity.IsAuthenticated` in `OnConnectedAsync`. Validate tenant access in `JoinTenantGroup` against user's JWT claims. Remove ConnectionId fallback — always require authenticated UserIdentifier
3. **PluginController**: Remove `[AllowAnonymous]` from `GetManifest()` — add `[Authorize]`
4. **TenantContextMiddleware**: Remove X-Tenant-Id header override. Tenant should come ONLY from JWT claims. If header override is business-required, validate that user has access to requested tenant before allowing
5. **BatchController**: Before processing each batch request, validate entity existence and check permissions (read/write as appropriate)
6. **AuthController**: Add simple rate limiting — track failed login attempts per IP using `IMemoryCache`, block after 5 failures for 15 minutes

### Acceptance Criteria
- [ ] $metadata returns 401 without valid token
- [ ] SignalR connections rejected without valid token
- [ ] Plugin manifest returns 401 without valid token
- [ ] Tenant header override validated against user permissions
- [ ] Auth endpoint returns 429 after repeated failures
- [ ] All existing tests pass (update tests that relied on unauthenticated access)

---

## Work Stream 4: Frontend Security

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Large

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| F1 | CRITICAL | Admin API key exposed in frontend JS bundle |
| F2 | CRITICAL | JWT tokens in localStorage (XSS accessible) |
| F3 | CRITICAL | Open redirect via query.redirect parameter |
| F4 | CRITICAL | Missing CSRF protection |
| F5 | CRITICAL | OAuth ID tokens not validated (no signature/nonce) |
| F6 | HIGH | OData filter field names not validated |
| F7 | HIGH | Weak OAuth state — no PKCE |
| F8 | HIGH | Tenant ID in localStorage — user can switch tenants |
| F9 | HIGH | Admin endpoints on login page |
| F10 | HIGH | Missing RBAC in route guards |

### Files to Modify
- `frontend/src/services/api.ts`
- `frontend/src/services/adminService.ts`
- `frontend/src/composables/useAuth.ts`
- `frontend/src/composables/useTenant.ts`
- `frontend/src/composables/useOAuth.ts`
- `frontend/src/utils/odataQueryBuilder.ts`
- `frontend/src/router/index.ts`
- `frontend/src/views/auth/LoginView.vue`
- `frontend/.env` / `frontend/.env.development`

### Tasks
1. **Remove admin API key from frontend**: Delete `VITE_ADMIN_API_KEY` from all .env files. Remove `adminKeyManager` from api.ts. Admin operations must go through authenticated backend endpoints with role check, not shared API key
2. **Token storage**: Move JWT tokens from localStorage to memory-only storage (reactive ref). Implement token refresh via httpOnly cookie set by backend. Remove `localStorage.getItem('access_token')` pattern
3. **Open redirect**: In useAuth.ts and useTenant.ts, validate redirect URL is relative before `router.push()`:
   ```typescript
   function isValidRedirect(url: string): boolean {
     try { return new URL(url, location.origin).origin === location.origin }
     catch { return false }
   }
   ```
4. **CSRF**: Add CSRF token handling — backend generates token in cookie, frontend reads and sends as `X-CSRF-Token` header on state-changing requests
5. **OAuth**: Move token exchange to backend. Frontend sends authorization code to backend, backend validates token (signature, nonce, expiration, issuer) and returns session. Never parse ID tokens client-side
6. **OData filter**: Validate field names against entity metadata before building filter expressions. Reject field names not in metadata
7. **Tenant ID**: Remove tenant switching from localStorage. Tenant comes from authenticated user context only
8. **Admin on login page**: Remove admin compile/bootstrap/clear buttons from LoginView.vue. Move to dedicated AdminView with role check
9. **Route guards**: Add `meta.requiredRoles` to admin routes. Check user roles in router beforeEach guard

### Acceptance Criteria
- [ ] No `VITE_ADMIN_API_KEY` anywhere in frontend code
- [ ] `localStorage` does not contain tokens (verify in browser devtools)
- [ ] `/?redirect=https://evil.com` redirects to `/dashboard` not external site
- [ ] OData filter rejects unknown field names
- [ ] Admin routes require admin role
- [ ] Login page has no admin operations
- [ ] All frontend tests pass

---

## Work Stream 5: Plugin System State Machine Fixes

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Medium

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| P1 | CRITICAL | Cache updated BEFORE lifecycle hook success |
| P2 | CRITICAL | Module install failure = silent success |
| P3 | CRITICAL | Bootstrap continues after dependency failure |
| P4 | CRITICAL | Connection reuse across failed migrations |
| P5 | HIGH | RequiresPluginAttribute uses sync cache (stale) |
| P6 | MEDIUM | JSON deserialization error crashes entire plugin system |

### Files to Modify
- `src/BMMDL.Runtime/Plugins/PluginManager.cs`
- `src/BMMDL.Runtime.Api/Middleware/RequiresPluginAttribute.cs`

### Tasks
1. **EnablePluginAsync/DisablePluginAsync**: Move `_stateCache[name] = state.Status` to AFTER successful lifecycle hook call. If hook throws, don't update cache
2. **InstallPluginAsync**: If `InstallPluginModulesAsync` returns any `!r.Success`, rollback plugin state (call UninstallPluginAsync) and throw `InvalidOperationException` with error details
3. **BootstrapBuiltInPluginsAsync**: Track failed plugins in `HashSet<string>`. Before processing each feature, check if any of its `DependsOn` are in the failed set. If so, skip with LogError and add to failed set. Log root causes vs cascading failures at different levels
4. **UninstallPluginAsync**: Create fresh connection per migration iteration instead of reusing single connection across the loop
5. **RequiresPluginAttribute**: Convert to `IAsyncActionFilter` and use `pluginManager.IsPluginEnabledAsync()` instead of synchronous cache check
6. **ReadPluginState**: Wrap `JsonSerializer.Deserialize` in try-catch. On `JsonException`, log warning and use empty `Dictionary<string, object?>()` instead of crashing

### Acceptance Criteria
- [ ] EnablePlugin with failing OnEnabledAsync does NOT update cache
- [ ] InstallPlugin with failing module compilation throws and rolls back
- [ ] Bootstrap skips dependents of failed plugins (verify with test)
- [ ] Each migration gets its own connection
- [ ] RequiresPluginAttribute checks DB asynchronously
- [ ] Corrupted settings JSON doesn't crash bootstrap
- [ ] All 2941+ existing tests pass

---

## Work Stream 6: Plugin Loader Concurrency Fixes

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Small

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| L1 | CRITICAL | Race condition in LoadPluginFromDirectory (TOCTOU) |
| L2 | CRITICAL | ValidateManifestDependencies called without lock |
| L3 | CRITICAL | Unsafe iterator in GetAvailableFeatureNames |
| L4 | CRITICAL | Non-atomic global feature activation |
| L5 | CRITICAL | File watcher TOCTOU race |

### Files to Modify
- `src/BMMDL.Runtime/Plugins/Loading/PluginDirectoryLoader.cs`
- `src/BMMDL.Runtime/Plugins/PlatformFeatureRegistry.cs`

### Tasks
1. **LoadPluginFromDirectory**: Move `ValidateManifestDependencies()` and `LoadPlugin()` INSIDE the lock block. Single lock acquisition for check-validate-load-register
2. **GetAvailableFeatureNames**: Copy `descriptor.Features` to local list inside lock before iterating
3. **ActivateGlobalFeature**: Use `_globalFeatures.TryAdd()` (already atomic). Validate feature exists before activation
4. **OnPluginDirectoryCreated**: Increase delay to 1000ms. Add file stability check (try exclusive open). Log clearly on each step. Don't swallow exceptions silently — log at Error level

### Acceptance Criteria
- [ ] Concurrent LoadPlugin calls for same plugin — only one succeeds, other throws
- [ ] GetAvailableFeatureNames doesn't throw during concurrent unload
- [ ] File watcher logs clear messages on load failure
- [ ] All existing tests pass

---

## Work Stream 7: Data Access & Thread Safety

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Medium

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| D1 | CRITICAL | Regex-based SQL parsing in temporal updates |
| D2 | CRITICAL | MetaModelCache dictionary mutations not thread-safe |
| D3 | CRITICAL | UnitOfWork tenant isolation breach in background tasks |
| D4 | HIGH | Missing command timeouts |
| D5 | HIGH | MetaModelCacheManager reload deadlock risk |
| D6 | HIGH | Filter state not restored on exception |
| D7 | HIGH | Ambiguous entity names silently overwrite |
| D8 | MEDIUM | Sensitive parameter values in exception data |

### Files to Modify
- `src/BMMDL.Runtime/DataAccess/ParameterizedQueryExecutor.cs`
- `src/BMMDL.Runtime/MetaModelCache.cs`
- `src/BMMDL.Runtime/MetaModelCacheManager.cs`
- `src/BMMDL.Runtime/DataAccess/UnitOfWork.cs`
- `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs`
- `src/BMMDL.Runtime.Api/Program.cs` (UoW registration)

### Tasks
1. **PatchInsertNullsWithOldValues**: Replace regex-based SQL parsing. Generate patched INSERT at query construction time (in DynamicSqlBuilder) rather than post-hoc regex on final SQL string. If regex approach must stay, handle nested parentheses and commas in string literals
2. **MetaModelCache**: Replace `Dictionary<string, BmEvent>` with `ConcurrentDictionary`. Or make cache fully immutable — all mutations create new cache instance
3. **UnitOfWork**: Defer tenantId resolution. Don't capture from HttpContext at DI factory time. Instead, resolve in `BeginAsync()` from current HttpContext. Throw if no HttpContext available (background task must explicitly set tenant)
4. **Command timeouts**: Add configurable `CommandTimeoutSeconds` (default 30 for reads, 300 for writes). Set on every NpgsqlCommand before execution
5. **MetaModelCacheManager**: Remove synchronous `Reload()` method. Force all callers to use `ReloadAsync()`. Fix double-check locking to check `_loadingTask` inside lock
6. **DynamicSqlBuilder**: Ensure filter state `Disable()` returns IDisposable that restores state even on exception. Add try-finally if `using` pattern is insufficient
7. **MetaModelCache constructor**: Log warning when duplicate entity names are detected during indexing. Don't silently overwrite
8. **ParameterizedQueryExecutor**: Log parameter NAMES and TYPES only in exception data, not VALUES. Remove `p.Value` from diagnostic string

### Acceptance Criteria
- [ ] Temporal updates work with string values containing commas
- [ ] MetaModelCache is thread-safe under concurrent access
- [ ] UnitOfWork throws if no HttpContext in background task
- [ ] All SQL commands have explicit timeout
- [ ] Duplicate entity names produce warning logs
- [ ] Parameter values not in exception data
- [ ] All existing tests pass

---

## Work Stream 8: Compiler & CodeGen Safety

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Medium

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| G1 | CRITICAL | CHECK constraint uses raw ConditionString (SQL injection) |
| G2 | CRITICAL | Unquoted FK columns in junction table DDL |
| G3 | CRITICAL | Migration identifier conversion — incomplete keyword list |
| G4 | HIGH | PK fallback to hardcoded "id" in subqueries |
| G5 | HIGH | Silent field loss on circular aspect dependency |
| G6 | HIGH | Type alias precision/scale parameters lost |
| G7 | HIGH | Incomplete SQL keyword list (22 vs 300+) |
| G8 | HIGH | Association target entity null returns null silently |

### Files to Modify
- `src/BMMDL.CodeGen/PostgresDdlGenerator.cs`
- `src/BMMDL.CodeGen/Schema/MigrationScriptGenerator.cs`
- `src/BMMDL.CodeGen/Visitors/PostgresSqlExpressionVisitor.cs`
- `src/BMMDL.Compiler/Pipeline/Passes/OptimizationPass.cs`
- `src/BMMDL.Compiler/Pipeline/Passes/SymbolResolutionPass.cs`

### Tasks
1. **GenerateCheckConstraint**: Remove `ConditionString` fallback. Require parsed `Condition` AST. If null, throw `InvalidOperationException("Constraint requires parsed condition AST")`
2. **GenerateJunctionTable**: Quote ALL column names: `NamingConvention.QuoteIdentifier(sourceFk)` for FK columns, UNIQUE constraints, and REFERENCES
3. **ConvertExpressionIdentifiers**: Replace `_sqlKeywords` with comprehensive PostgreSQL reserved word list (100+ words from official docs). Quote all converted identifiers via `QuoteIdentifier()`
4. **TryResolveAssociationNavigation**: Remove `"id"` fallback. If `pkField == null`, throw `InvalidOperationException("Entity has no primary key")`
5. **ResolveAspectChain**: On circular dependency detection, fail compilation immediately. Don't `continue` — return error that stops inlining for that entity entirely
6. **Type alias resolution**: Preserve full type string including parameters. Parse `Decimal(18,2)` into separate `BaseTypeName="Decimal"`, `Precision=18`, `Scale=2`. Pass through to DDL generation
7. **MapTypeToPostgres**: Use centralized `SqlTypeMapper` instead of local 7-type mapping. Ensure all BMMDL types are covered
8. **Association target null**: Throw `InvalidOperationException` if entity resolver returns null, don't return null silently

### Acceptance Criteria
- [ ] CHECK constraints without parsed AST cause compilation error
- [ ] Junction tables generate valid DDL for entities with reserved-word FK names
- [ ] Migration scripts handle fields named `sequence`, `check`, `constraint` etc.
- [ ] Entity without PK causes clear error, not silent "id" fallback
- [ ] Circular aspects cause compilation failure, not silent field loss
- [ ] Type alias `Price: Decimal(18,2)` generates `numeric(18,2)` not `numeric`
- [ ] All existing tests pass + new tests for each fix

---

## Work Stream 9: Runtime Services Concurrency

**Owner**: TBD
**Priority**: P0 (CRITICAL)
**Estimated effort**: Small

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| R1 | CRITICAL | AsyncLocal context leak in StatementExecutor |
| R2 | CRITICAL | MetaModelCacheManager double-check locking race |
| R3 | CRITICAL | Infinite recursion in RuntimeExpressionEvaluator |
| R4 | HIGH | SQL string interpolation in SequenceService |
| R5 | HIGH | Event dispatch race in UnitOfWork |
| R6 | HIGH | Exception not recorded in CallStatement resilient mode |
| R7 | HIGH | Unsafe collection enumeration in CallTargetResolver |

### Files to Modify
- `src/BMMDL.Runtime/Rules/StatementExecutor.cs`
- `src/BMMDL.Runtime/MetaModelCacheManager.cs`
- `src/BMMDL.Runtime/Expressions/RuntimeExpressionEvaluator.cs`
- `src/BMMDL.Runtime/Services/SequenceService.cs`
- `src/BMMDL.Runtime/DataAccess/UnitOfWork.cs`
- `src/BMMDL.Runtime/Services/CallTargetResolver.cs`

### Tasks
1. **StatementExecutor**: Initialize `_activeCallChain.Value` to new `HashSet<string>()` at method entry. Set to `null` in `finally` block. This ensures each async context gets a clean chain
2. **MetaModelCacheManager**: Check `_loadingTask` inside lock before awaiting. Remove synchronous `Reload()` — force async
3. **RuntimeExpressionEvaluator**: Add `const int MAX_NAVIGATION_DEPTH = 20`. Pass depth counter through `ResolveNavigationPath` and `NavigateDict`. Throw if exceeded
4. **SequenceService**: Replace string interpolation `$"SET LOCAL app.tenant_id = '{tenantId}'"` with parameterized query
5. **UnitOfWork.CommitAsync**: Set `_committed = true` BEFORE publishing events (transaction already committed). Wrap event dispatch in try-catch — log errors but don't rethrow
6. **StatementExecutor** (call error): In generic catch block when `ContinueOnCallError=true`, set `result.Rejected = true` and add error to `result.Errors`
7. **CallTargetResolver**: Snapshot collections before iterating: `var services = _cache.Services.ToList()`

### Acceptance Criteria
- [ ] AsyncLocal chain cleaned up even on exceptions
- [ ] No deadlock possible in MetaModelCacheManager
- [ ] Expression evaluation with 50+ navigation depth throws clear error
- [ ] No string interpolation in SQL statements
- [ ] Failed event dispatch doesn't crash request
- [ ] Call errors recorded in resilient mode
- [ ] CallTargetResolver stable during cache reload
- [ ] All existing tests pass

---

## Work Stream 10: Input Validation & Edge Cases

**Owner**: TBD
**Priority**: P1 (HIGH)
**Estimated effort**: Medium

### Issues Addressed
| ID | Severity | Issue |
|----|----------|-------|
| V1 | HIGH | File upload MIME validation bypassable (header only) |
| V2 | HIGH | Path traversal in plugin loading |
| V3 | HIGH | Deep operation nested input unvalidated |
| V4 | MEDIUM | Missing enum value validation at compile time |
| V5 | MEDIUM | Inherited field redefinition not detected |
| V6 | MEDIUM | TOCTOU in CommentStore.ToggleLikeAsync |
| V7 | MEDIUM | Missing CSRF protection on state-changing endpoints |
| V8 | MEDIUM | Request size limits missing on some endpoints |

### Files to Modify
- `src/BMMDL.Runtime.Api/Controllers/FileStorageController.cs`
- `src/BMMDL.Runtime.Api/Controllers/PluginController.cs`
- `src/BMMDL.Runtime.Api/Handlers/DeepOperationBase.cs`
- `src/BMMDL.Compiler/Validation/FieldTypeValidator.cs`
- `src/BMMDL.CodeGen/PostgresDdlGenerator.cs`
- `src/BMMDL.Runtime/Collaboration/Comment.cs`
- `src/BMMDL.Runtime.Api/Program.cs`

### Tasks
1. **FileStorageController**: Add magic byte validation for common file types (PDF, PNG, JPEG, DOCX). Don't rely solely on Content-Type header
2. **PluginController**: Validate that plugin path is within the configured plugins directory. Use `Path.GetFullPath()` and check `StartsWith(pluginsDir)`
3. **DeepOperationBase**: Validate navigation property names match entity definition exactly. Reject unknown nested object keys
4. **FieldTypeValidator**: When default is EnumValue, validate the value exists in the referenced enum's Values list. Add error code `SEM_INVALID_ENUM_VALUE`
5. **PostgresDdlGenerator**: In table-per-type inheritance, detect and error on child entity field redefinition of parent fields
6. **CommentStore.ToggleLikeAsync**: Use SQL `array_append`/`array_remove` instead of read-modify-write pattern to avoid TOCTOU race
7. **Program.cs**: Add `[RequestSizeLimit(10_000_000)]` to POST/PATCH endpoints, or configure globally via Kestrel `MaxRequestBodySize`
8. **Anti-forgery**: Add `services.AddAntiforgery()` and validate tokens on state-changing endpoints

### Acceptance Criteria
- [ ] File with .exe extension and PDF magic bytes rejected
- [ ] Plugin load from `/etc/shadow` directory throws
- [ ] Deep insert with unknown navigation key returns 400
- [ ] Invalid enum default value causes compilation error
- [ ] Child entity redefining parent field causes compilation error
- [ ] Concurrent like/unlike operations don't lose data
- [ ] All existing tests pass

---

## Execution Plan

```
Week 1 (Parallel):
  WS1: Secrets & Config ─────────────── [Small]
  WS2: HTTP Security ────────────────── [Medium]
  WS3: Auth & AuthZ Gaps ────────────── [Medium]
  WS5: Plugin State Machine ─────────── [Medium]
  WS6: Plugin Loader Concurrency ────── [Small]
  WS9: Runtime Concurrency ──────────── [Small]

Week 2 (Parallel):
  WS4: Frontend Security ────────────── [Large]
  WS7: Data Access & Thread Safety ──── [Medium]
  WS8: Compiler & CodeGen Safety ────── [Medium]
  WS10: Input Validation ────────────── [Medium]
```

All work streams are independent — no cross-dependencies.
Each work stream should create a feature branch, run full test suite, then PR to main.

## Branch Naming Convention

```
fix/ws1-secrets-hardening
fix/ws2-http-security
fix/ws3-auth-gaps
fix/ws4-frontend-security
fix/ws5-plugin-state-machine
fix/ws6-plugin-loader-concurrency
fix/ws7-data-access-thread-safety
fix/ws8-compiler-codegen-safety
fix/ws9-runtime-concurrency
fix/ws10-input-validation
```

## Test Requirements

Each work stream MUST:
1. Not break any existing tests (2941+ unit tests)
2. Add new tests for each fix (estimate 5-15 tests per work stream)
3. Run `dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj` before PR
4. Document any breaking changes

## Verification

After all work streams merge:
1. Run full test suite: `dotnet test BMMDL.sln`
2. Manual verification of key security fixes (SQL leak, auth, CORS)
3. Deploy to staging and run E2E tests
