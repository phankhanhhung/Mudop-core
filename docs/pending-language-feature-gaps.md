# BMMDL Pending Feature Gaps

> Fresh audit: 2026-02-15
> Scope: Grammar + Compiler + CodeGen + Runtime (end-to-end)
> Previous plan status: P1 DONE, P2 DONE, P3 DONE (17/22 items closed)

This document catalogs **only remaining gaps** found during a fresh full-stack audit.

---

## 1. FunctionRegistry — Grammar/Runtime Name Mismatches & Missing Functions

**Severity: HIGH** | **Effort: Small**

The grammar (`BmmdlParser.g4:738-749`) defines function names that either don't exist in `FunctionRegistry.cs` or use different naming conventions.

### Missing Functions (no implementation at all)

| Grammar Token | Category | Notes |
|---------------|----------|-------|
| `TRUNC` | Math | Numeric truncation (`TRUNC(3.7)` → `3`) |
| `SIGN` | Math | Returns -1, 0, or 1 |
| `CEIL` | Math | Only `CEILING` exists — no `CEIL` alias |
| `WEEKOFYEAR` | Date | ISO week number |
| `CURRENT_DATE` | Date | Should return current date |
| `CURRENT_TIME` | Date | Should return current time |
| `CURRENT_TIMESTAMP` | Date | Should return current timestamp |
| `FIRST` | Aggregate | First value in collection |
| `LAST` | Aggregate | Last value in collection |
| `NEXT_SEQUENCE` | Sequence | Get next sequence value |
| `CURRENT_SEQUENCE` | Sequence | Get current sequence value |
| `FORMAT_SEQUENCE` | Sequence | Format sequence output |

### Name Mismatches (grammar has underscores, registry doesn't)

| Grammar Name | Registry Name | Fix |
|-------------|---------------|-----|
| `ADD_DAYS` | `ADDDAYS` | Add alias |
| `ADD_MONTHS` | `ADDMONTHS` | Add alias |
| `ADD_YEARS` | `ADDYEARS` | Add alias |
| `PAD_LEFT` | `PADLEFT` | Add alias (LPAD exists) |
| `PAD_RIGHT` | `PADRIGHT` | Add alias (RPAD exists) |

**File:** `src/BMMDL.Runtime/Expressions/FunctionRegistry.cs`

---

## 2. Expression Path Navigation — Not Implemented

**Severity: MEDIUM** | **Effort: Large**

Cross-entity path expressions like `order.customer.name` are parsed but never resolved.

- **BindingPass** (`src/BMMDL.Compiler/Pipeline/Passes/BindingPass.cs:160`): Explicit comment — *"Path navigation (e.g. Order.Customer.Name) logic would go here. For now implementing simple binding as per plan"*
- **ExpressionDependencyPass** (`src/BMMDL.Compiler/Pipeline/Passes/ExpressionDependencyPass.cs:152`): Only tracks simple identifiers within current entity, cannot follow association paths
- **Impact**: Computed fields, rules, and access control conditions that reference related entity fields are not validated at compile time and may fail at runtime

---

## 3. DynamicServiceController — Incomplete Statement Execution

**Severity: MEDIUM** | **Effort: Medium**

`src/BMMDL.Runtime.Api/Controllers/DynamicServiceController.cs`

| Line | Gap |
|------|-----|
| 294 | Optional action/function parameters not supported — all treated as required |
| 340 | Only `BmComputeStatement` executed in action bodies; other statement types (validate, when, call, foreach, let, raise) ignored. Comment: *"TODO: Integrate with RuleEngine.ExecuteRulesAsync for full statement execution"* |

**Note:** `ODataServiceController.cs:279` has the same optional parameter gap.

---

## 4. OAuth Provider Validation — Microsoft & Apple Stubs

**Severity: MEDIUM** | **Effort: Medium**

`src/BMMDL.Runtime.Api/Services/OAuthValidatorService.cs`

| Line | Gap |
|------|-----|
| 126-136 | Microsoft/Azure AD token validation — returns `null` (always fails). Needs `Microsoft.Identity.Web` or manual JWT+JWKS validation. |
| 146-156 | Apple Sign-In token validation — returns `null` (always fails). Needs Apple public key verification (`https://appleid.apple.com/auth/keys`). |

Google OAuth works. Only Microsoft and Apple are stubs.

---

## 5. Tenant Module Management — All Stubs

**Severity: MEDIUM** | **Effort: Large**

`src/BMMDL.Runtime.Api/Controllers/TenantController.cs`

| Line | Gap |
|------|-----|
| 134 | `CreateTenant` — Owner ID hardcoded to current user (`TODO: Get actual owner`) |
| 221 | `GetInstalledModules` — Returns hardcoded placeholder `["Platform v1.0.0"]` instead of querying registry |
| 248-250 | `InstallModule` — 3 stubs: load from registry, generate DDL, register installation. Returns fake 201. |

---

## 6. Dead Grammar Tokens — 44 Unused Lexer Keywords

**Severity: LOW** | **Effort: Small (cleanup)**

These tokens exist in `BmmdlLexer.g4` with **no parser rules** referencing them:

| Category | Count | Tokens |
|----------|-------|--------|
| Window functions | 16 | `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE`, `LAG`, `LEAD`, `FIRST_VALUE`, `LAST_VALUE`, `OVER`, `PARTITION`, `ROWS`, `RANGE`, `ROW`, `UNBOUNDED`, `PRECEDING`, `FOLLOWING` |
| Temporal / History | 6 | `HISTORY`, `TRANSACTION`, `VALID`, `PERIOD`, `FISCAL_PERIOD`, `FISCAL_YEAR` |
| Operators | 8 | `AMPERSAND` (`&`), `ARROW` (`->`), `DOUBLE_ARROW` (`=>`), `DOUBLE_COLON` (`::`), `CARET` (`^`), `TILDE` (`~`), `EXCLAIM` (`!`), `PIPE` (`\|`) |
| Reserved / Misc | 14 | `ABORT`, `BOM`, `IF`, `JOINED`, `LOG`, `MANY`, `ONE`, `REF`, `RESET_SEQUENCE`, `SEALED`, `SESSION`, `SET_SEQUENCE`, `SHARED`, `TABLE` |

**Decision needed:** Remove dead tokens or implement corresponding parser rules (window functions would be the most valuable).

---

## 7. FlattenedField Default Values — Disabled

**Severity: LOW** | **Effort: Small**

`src/BMMDL.CodeGen/FlattenedField.cs:36-40` — DEFAULT clause generation for structured type flattened fields is commented out:
```csharp
// TODO: FlattenedField defaults may contain column references
// Disable for now until SafeDefaultValue is applied here
```

**Impact:** Flattened fields from structured types don't get DEFAULT values in DDL.

---

## 8. Compiler CLI `codegen` Command — Stub

**Severity: LOW** | **Effort: Small**

`src/BMMDL.Compiler/Program.cs:116` — The `codegen` CLI command logs a warning and does nothing:
```csharp
logger.LogWarning("Code generation not yet implemented");
// TODO: Implement code generation
```

**Impact:** CLI users cannot generate DDL from command line; only the Admin API path works.

---

## 9. Deprecated Method — `UpdateUserTenantAsync`

**Severity: LOW** | **Effort: Trivial**

`src/BMMDL.Runtime/Services/DynamicPlatformUserService.cs:502` — Throws `NotImplementedException`. Comment says it's deprecated since User is tenant-scoped via RLS.

**Action:** Remove the method and its interface declaration.

---

## 10. Skipped Grammar Features (Intentional)

These were evaluated and deliberately skipped:

| ID | Feature | Reason |
|----|---------|--------|
| A3 | `contextDef` | Namespace blocks provide equivalent functionality |
| A6 | `temporalQualifier` in view SELECT | Runtime handles temporal via `asOf`/`validAt` query params |

---

## Priority Matrix

| # | Feature | Impact | Effort | Priority |
|---|---------|--------|--------|----------|
| 1 | FunctionRegistry missing functions + aliases | HIGH | Small | **P4** |
| 2 | Expression path navigation | MEDIUM | Large | **P5** |
| 3 | DynamicServiceController full statement execution | MEDIUM | Medium | **P4** |
| 4 | OAuth Microsoft/Apple validation | MEDIUM | Medium | **P4** |
| 5 | Tenant module management | MEDIUM | Large | **P5** |
| 6 | Dead grammar tokens cleanup | LOW | Small | **P5** |
| 7 | FlattenedField defaults | LOW | Small | **P5** |
| 8 | CLI codegen command | LOW | Small | **P5** |
| 9 | Remove deprecated method | LOW | Trivial | **P5** |

### P4 — Next Priority (correctness + completeness)
- **#1** FunctionRegistry: 12 missing functions + 5 underscore aliases
- **#3** DynamicServiceController: wire RuleEngine for full action body execution + optional params
- **#4** OAuth: Microsoft and Apple token validation

### P5 — Low Priority (cleanup + nice-to-have)
- **#2** Expression path navigation (large effort, niche use case currently)
- **#5** Tenant module management (admin feature, manual workaround exists)
- **#6-9** Cleanup items

---

## Compiler Validation Status (P3 Fixes Verified)

The following 7 validation gaps from the previous audit are **confirmed fixed** (SEM060-SEM066):
- Enum value uniqueness (`SEM060`)
- Index column existence (`SEM061`)
- Cardinality bounds (`SEM062`)
- Default expression type checking (`SEM063`)
- CHECK constraint field validation (`SEM064`)
- Function/action parameter type resolution (`SEM065`)
- Type base type resolution (`SEM066`)

## Runtime TODO Count

**14 TODO comments** remain across the codebase (down from 30+ pre-P3).
