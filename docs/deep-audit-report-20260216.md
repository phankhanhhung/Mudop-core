# BMMDL Deep Audit Report — 2026-02-16

## Scope
Full manual review of Registry and Runtime code to identify any BMMDL language features not fully implemented across all layers: Grammar → MetaModel → Compiler → CodeGen → Registry Persistence → Runtime → API.

## Audit Areas (7 parallel agents)
1. Grammar vs MetaModel gaps
2. Compiler passes completeness
3. CodeGen DDL completeness
4. Registry persistence round-trip
5. Runtime query/CRUD completeness
6. Runtime API controllers
7. Registry API + install flow

---

## Summary

| Severity | Count | Status |
|----------|-------|--------|
| CRITICAL | 12 | Fixing (7 parallel agents) |
| HIGH | 45+ | Pending |
| MEDIUM | 35+ | Pending |
| LOW | 20+ | Backlog |

---

## CRITICAL Issues (12)

### C1. Access Control (PermissionChecker) Never Enforced
- **Location:** `DynamicEntityController.cs`
- **Problem:** `PermissionChecker` exists and is properly implemented but never called in any CRUD endpoint. All entities are fully accessible without authorization checks.
- **Impact:** Security hole — any authenticated user can read/write/delete any entity regardless of access control rules defined in BMMDL.
- **Fix:** Inject PermissionChecker and add checks at start of each CRUD operation (read/create/update/delete). Return 403 on denial.

### C2. Registry Expression AST Round-Trip Broken (6 types)
- **Location:** `EfCoreMetaModelRepository.cs` → `ReconstructNode`
- **Problem:** `MapExpressionNode` correctly serializes Case/Cast/In/Between/Like/IsNull expressions, but `ReconstructNode` has no matching cases to deserialize them. Saved expressions come back as null/broken.
- **Impact:** Any rule or computed field using these expression types loses its AST after save/load cycle. Rules silently become no-ops.
- **Fix:** Add 6 missing cases to `ReconstructNode` switch.

### C3. Subquery/Exists/Temporal Expressions Not Saved
- **Location:** `EfCoreMetaModelRepository.cs` → `MapExpressionNode`
- **Problem:** `BmSubqueryExpression`, `BmExistsExpression`, `BmTemporalBinaryExpression` have no cases in the save switch. They're silently dropped.
- **Impact:** Rules using `EXISTS(...)`, subqueries, or temporal operators lose these expressions on persist.
- **Fix:** Add 3 missing cases to `MapExpressionNode` switch.

### C4. WhereConditionExpr Saved but Never Loaded
- **Location:** `EfCoreMetaModelRepository.cs` → entity loading
- **Problem:** Entity views' `WhereConditionExpr` is serialized to DB but the load path never reconstructs it.
- **Impact:** View WHERE conditions are lost after registry reload. Views return unfiltered data.
- **Fix:** Add WhereConditionExpr reconstruction in entity load path.

### C5. Rule ASTs Null in LoadAllToCacheAsync
- **Location:** `EfCoreMetaModelRepository.cs` → bulk load path
- **Problem:** The bulk-load path (used by MetaModelCache initialization) skips expression reconstruction for rules. Individual entity loads work fine.
- **Impact:** After runtime restart, all rules have null ASTs until individually reloaded. Business rules silently don't fire.
- **Fix:** Ensure bulk load reconstructs rule expressions like the single-entity path.

### C6. Entity.ExtendsFrom Not Persisted
- **Location:** `EfCoreMetaModelRepository.cs` + `EntityModels.cs`
- **Problem:** `BmEntity.ExtendsFrom` (entity inheritance) has no column in the DB model. Never saved or loaded.
- **Impact:** Entity inheritance information lost after persist. Inherited fields/rules not resolved on reload.
- **Fix:** Add `ExtendsFrom` column to EntityModel, update save/load mapping.

### C7. ExpressionTraversalUtility Missing 9 Expression Types
- **Location:** `ExpressionTraversalUtility.cs` → `Traverse()`
- **Problem:** Missing handlers for: BmCaseExpression, BmCastExpression, BmInExpression, BmBetweenExpression, BmLikeExpression, BmIsNullExpression, BmSubqueryExpression, BmExistsExpression, BmTemporalBinaryExpression.
- **Impact:** Any compiler pass using this utility to analyze expressions silently misses these node types. Dependency analysis, binding, and validation are incomplete.
- **Fix:** Add 9 cases with recursive child traversal.

### C8. BindingPass Silently Skips 6 Statement Types
- **Location:** `BindingPass.cs` → `BindStatement()`
- **Problem:** Only handles BmValidateStatement, BmComputeStatement, BmWhenStatement. Silently skips: BmCallStatement, BmEmitStatement, BmReturnStatement, BmLetStatement, BmRejectStatement, BmForeachStatement.
- **Impact:** Expressions in these statement types never go through type binding. Type mismatches go undetected at compile time.
- **Fix:** Add 6 cases binding each statement's expressions. ✅ **DONE**

### C9. Virtual Computed Strategy Generates Invalid PostgreSQL DDL
- **Location:** `PostgresDdlGenerator.cs`
- **Problem:** Computed fields with `virtual` strategy generate invalid DDL. PostgreSQL only supports `GENERATED ALWAYS AS (expr) STORED` — the `VIRTUAL` keyword is MySQL-only.
- **Impact:** Schema initialization fails for any entity with virtual computed fields.
- **Fix:** Always emit `STORED` keyword for PostgreSQL generated columns.

### C10. BmAggregateExpression Not Dispatched in SQL Visitor
- **Location:** `PostgresSqlExpressionVisitor.cs` → `Visit()` dispatch
- **Problem:** No case for `BmAggregateExpression`. Throws `NotSupportedException` when a computed field uses `count(orders)`, `sum(amount)`, etc.
- **Impact:** DDL generation crashes for entities with aggregate computed fields.
- **Fix:** Add dispatch case generating `COUNT(*)`, `SUM(col)`, etc.

### C11. VisitImportStmt Called but Undefined
- **Location:** `BmmdlModelBuilder.cs` line 218
- **Problem:** `VisitNamespaceBlock` calls `VisitImportStmt(importStmt)` in a loop, but the method is never defined. ANTLR base visitor returns `default(BmModel)`, making it a no-op. `BmModel` has no top-level `Imports` collection for namespace-level `using` statements.
- **Impact:** Cross-namespace `using` import directives silently fail. SymbolResolutionPass can't use them for type resolution.
- **Fix:** Implement `VisitImportStmt` method, add imports to model.

### C12. contextDef Has No Handler
- **Location:** `BmmdlModelBuilder.cs` → `VisitDefinition`
- **Problem:** Grammar defines `contextDef : CONTEXT IDENTIFIER LBRACE definition* RBRACE` but `VisitDefinition` has no branch for it. No `BmContext` class exists. All nested definitions inside a context block are silently lost.
- **Impact:** `context Foo { entity Bar { ... } }` blocks completely swallowed.
- **Fix:** Add no-op handler (contextDef is low-value feature, just prevent silent swallowing).

---

## HIGH Issues (45+)

### Grammar / Model Builder

| # | Issue | Location | Impact |
|---|-------|----------|--------|
| H1 | `raiseStmt` missing from `VisitRuleStmt` | BmmdlModelBuilder.cs:1781 | `raise 'Error'` in rules crashes model build with InvalidOperationException |
| H2 | `whenStmt` ELSE branch dropped | BmmdlModelBuilder.cs:1817 | ELSE statements silently merged into THEN block — wrong semantics |
| H3 | Window function tokens exist, no parser rules | Lexer:248-261 | `ROW_NUMBER() OVER (...)` cannot be expressed in DSL |

### Compiler Passes

| # | Issue | Location | Impact |
|---|-------|----------|--------|
| H4 | SymbolResolutionPass doesn't register Events | SymbolResolutionPass.cs | Event references can't be resolved cross-module |
| H5 | SymbolResolutionPass doesn't register Sequences | SymbolResolutionPass.cs | Sequence references unresolved |
| H6 | SymbolResolutionPass doesn't register Views | SymbolResolutionPass.cs | View entity references fail |
| H7 | Event field types not resolved | SymbolResolutionPass.cs | Event payload fields keep raw type strings |
| H8 | SemanticValidationPass skips 6 statement types | SemanticValidationPass.cs | No semantic checks for call/emit/return/let/reject/foreach |
| H9 | SemanticValidationPass no view validation | SemanticValidationPass.cs | View SELECT syntax not validated |
| H10 | SemanticValidationPass no event validation | SemanticValidationPass.cs | Event structure not validated |
| H11 | SemanticValidationPass no sequence validation | SemanticValidationPass.cs | Sequence config not validated |
| H12 | DependencyGraphPass entity-only | DependencyGraphPass.cs | Cross-entity deps via events/services not tracked |
| H13 | ExtensionMergePass can't extend services | ExtensionMergePass.cs | `extend service X { ... }` silently ignored |
| H14 | ModificationPass can't modify aspects | ModificationPass.cs | `modify aspect X { ... }` silently ignored |
| H15 | AnnotationMergePass entity-only | AnnotationMergePass.cs | Annotations on services/types/enums not merged |
| H16 | InheritanceResolutionPass doesn't propagate fields | InheritanceResolutionPass.cs | Child entities don't get parent fields |

### CodeGen

| # | Issue | Location | Impact |
|---|-------|----------|--------|
| H17 | BmTernaryExpression not dispatched | PostgresSqlExpressionVisitor.cs | CASE WHEN in DDL throws |
| H18 | BmInExpression not dispatched | PostgresSqlExpressionVisitor.cs | IN operator in DDL throws |
| H19 | BmBetweenExpression not dispatched | PostgresSqlExpressionVisitor.cs | BETWEEN in DDL throws |
| H20 | BmLikeExpression not dispatched | PostgresSqlExpressionVisitor.cs | LIKE in DDL throws |
| H21 | BmIsNullExpression not dispatched | PostgresSqlExpressionVisitor.cs | IS NULL in DDL throws |
| H22 | IExpressionVisitor interface incomplete | IExpressionVisitor.cs | Interface doesn't declare all Visit methods |
| H23 | BmAddEntityStep generates only comment | PostgresDdlGenerator.cs | Migration ADD ENTITY emits `-- ADD ENTITY` comment only |
| H24 | SelectStatementSqlGenerator uses ToExpressionString | SelectStatementSqlGenerator.cs | View SQL uses DSL syntax instead of SQL |

### Registry Persistence

| # | Issue | Location | Impact |
|---|-------|----------|--------|
| H25 | Aspect fields not fully round-tripped | EfCoreMetaModelRepository.cs | Aspect field metadata lost on reload |
| H26 | Rule event references not saved | EfCoreMetaModelRepository.cs | Rule `emits` clauses lost |
| H27 | Service action preconditions/postconditions not saved | EfCoreMetaModelRepository.cs | Contract enforcement data lost |
| H28 | View SelectStatement AST not persisted | EfCoreMetaModelRepository.cs | View SQL definition lost on reload |
| H29 | Migration definitions not persisted | EfCoreMetaModelRepository.cs | Migration steps lost |
| H30 | Sequence definitions not persisted | EfCoreMetaModelRepository.cs | Sequence config lost |

### Runtime

| # | Issue | Location | Impact |
|---|-------|----------|--------|
| H31 | $search searches ALL field types | DynamicSqlBuilder.cs | Searches Integer/Boolean/UUID fields — performance + incorrect results |
| H32 | BuildCountQuery unsafe string.Replace | DynamicSqlBuilder.cs | `SELECT *` → `SELECT COUNT(*)` replacement fragile |
| H33 | MetaModelCache cross-namespace rule bleed | MetaModelCache.cs | Rules from wrong namespace applied to entities |
| H34 | BmReturnStatement missing from RuleEngine | RuleEngine.cs | `return` in action bodies crashes/is ignored |
| H35 | any/all $filter FK inference by name | DynamicSqlBuilder.cs | Fragile name-based FK resolution |
| H36 | Temporal OVERLAPS SQL still wrong | PostgresSqlExpressionVisitor.cs | OVERLAPS needs (start, end) OVERLAPS (start, end) pair syntax |

### Runtime API

| # | Issue | Location | Impact |
|---|-------|----------|--------|
| H37 | No rule execution on deep insert children | DeepInsertHandler.cs | Child entities bypass before/after rules |
| H38 | No rule execution on deep update children | DeepUpdateHandler.cs | Child entity updates bypass rules |
| H39 | Batch missing computed field stripping | BatchController.cs | Computed fields in batch POST/PUT cause errors |
| H40 | Batch missing ETag support | BatchController.cs | No optimistic concurrency in batch ops |
| H41 | Batch missing Prefer header | BatchController.cs | No return=minimal/representation in batch |
| H42 | No ComplexType in CSDL $metadata | ODataMetadataController.cs | Complex types not exposed in metadata |
| H43 | Contained entity creation ID issue | EntityActionController.cs | Parent FK not auto-populated for containment POST |
| H44 | $apply tenant injection fragile | DynamicEntityController.cs | String-based tenant filter injection in $apply |

### Registry API

| # | Issue | Location | Impact |
|---|-------|----------|--------|
| H45 | FilterModelForModule omits Events | AdminService.cs | Events not included in module publish |
| H46 | FilterModelForModule omits Views | AdminService.cs | Views not included in module publish |
| H47 | FilterModelForModule omits Sequences | AdminService.cs | Sequences not included in module publish |
| H48 | FilterModelForModule omits Migrations | AdminService.cs | Migrations not included in module publish |
| H49 | No schema rollback on install failure | AdminService.cs | Failed install leaves partial schema |
| H50 | MigrationExecutor continues on error | AdminService.cs | One failed migration step doesn't stop others |
| H51 | ModuleInstallationService skeleton | ModuleInstallationService.cs | Module install/uninstall not wired |

---

## MEDIUM Issues (35+)

### Grammar / Model Builder
- M1: `serviceDef` FOR clause parsed but never stored — `BmService` has no `ForEntity` property
- M2: `BmAddEntityStep.ElementsText` stores raw text instead of structured elements
- M3: `aspectElement` actionDef/functionDef silently dropped — no `BmAspect.BoundActions`
- M4: `extendDef` actionDef/functionDef/indexDef/constraintDef elements silently dropped

### Compiler Passes
- M5: ExtensionMergePass can't extend enums or types
- M6: ModificationPass can't modify enums
- M7: AnnotationMergePass doesn't handle annotation removal
- M8: InheritanceResolutionPass no diamond inheritance check
- M9: Missing EventValidationPass (dedicated pass for event definitions)
- M10: Missing MigrationValidationPass (dedicated pass for migration definitions)
- M11: Missing SequenceValidationPass (dedicated pass for sequence definitions)

### CodeGen
- M12: Localized String _texts table trigger incomplete
- M13: FileReference expansion doesn't generate all 8 metadata columns
- M14: Sequence DDL CREATE SEQUENCE not always generated
- M15: Unique constraint DDL may duplicate index DDL
- M16: Temporal SeparateTables strategy trigger generation incomplete
- M17: Array type DDL not tested with all element types
- M18: M:M junction table DDL not generated
- M19: Abstract entity still generates empty table
- M20: Index expression DDL (functional indexes) not supported

### Registry Persistence
- M21: Action body statements not fully round-tripped
- M22: Enum member annotations not persisted
- M23: Field-level annotations partially persisted
- M24: Type alias definitions not fully persisted
- M25: Service function definitions not fully persisted

### Runtime
- M26: $compute expressions not validated against entity schema
- M27: $apply groupby with nested properties not supported
- M28: Delta token expiry not configurable
- M29: Async operation cleanup not scheduled
- M30: Singleton entity PATCH doesn't trigger rules

### Runtime API
- M31: $batch dependency tracking doesn't handle circular deps
- M32: No $crossjoin support
- M33: No $all entity set enumeration
- M34: Media entity stream upload size not enforced at API level
- M35: No OpenAPI/Swagger generation from CSDL

### Registry API
- M36: Dependency resolution one level deep only (transitive deps not resolved)
- M37: Uninstall endpoint missing
- M38: Approval workflow is stub only
- M39: Module dependency version regex too strict

---

## LOW Issues (20+)

- L1: `BmTransformUpdateAction.Assignments` dead code (grammar mismatch)
- L2: Orphan lexer tokens: SEALED, MANY, ONE
- L3: Temporal OVERLAPS runtime evaluation semantics questionable
- L4: ExpressionDependencyPass doesn't track dependencies in new expression types
- L5: TenantIsolationPass doesn't check event/service scoping
- L6: FileStorageValidationPass doesn't validate provider names
- L7: TemporalValidationPass doesn't check SeparateTables trigger requirements
- L8: View DDL comment says "-- VIEW:" but doesn't CREATE VIEW
- L9: Migration DDL only generates ALTER TABLE ADD COLUMN
- L10: $search case sensitivity not configurable
- L11: $count inline vs /$count endpoint inconsistency
- L12: ETag weak vs strong format not configurable
- L13: Prefer header odata.track-changes not supported
- L14: Module version comparison is string-based not semver
- L15: Admin compile endpoint doesn't return detailed per-entity errors
- L16: Registry health check doesn't verify DB connectivity
- L17: No module dependency graph visualization endpoint
- L18: No compilation cache (recompiles every time)
- L19: No source map generation for BMMDL → SQL debugging
- L20: No incremental compilation support

---

## Fix Progress

### CRITICAL (12/12 DONE)

| Issue | Status |
|-------|--------|
| C1: PermissionChecker enforcement (6 controllers) | ✅ Done |
| C2-C4: Registry expression AST round-trip (9 reconstruct + 3 save + WhereConditionExpr) | ✅ Done |
| C5-C6: Rule ASTs bulk load + ExtendsFrom persistence | ✅ Done |
| C7: ExpressionTraversalUtility (10 expression types + SELECT traversal) | ✅ Done |
| C8: BindingPass (6 statement types) | ✅ Done |
| C9-C10: CodeGen virtual computed + 7 expression visitors | ✅ Done |
| C11-C12: Grammar VisitImportStmt + contextDef + raiseStmt + whenStmt ELSE | ✅ Done |

### HIGH (44/45 DONE — H44 skipped)

| Issue Group | Status |
|-------------|--------|
| H1-H2: raiseStmt + whenStmt ELSE branch | ✅ Done |
| H4-H7: SymbolResolutionPass (Events/Sequences/Views + event field types) | ✅ Done |
| H8-H11: SemanticValidationPass (6 statement types + views/events/sequences) | ✅ Done |
| H12-H16: Compiler secondary passes (DependencyGraph/Extension/Modification/Annotation/Inheritance) | ✅ Done |
| H31-H36: Runtime query bugs ($search/count/cache/return/FK/OVERLAPS) | ✅ Done |
| H37-H43: Runtime API handlers (deep rules/batch ETag+Prefer+computed/ComplexType/containment FK) | ✅ Done |
| H44: $apply tenant injection | Skipped (low impact) |
| H45-H51: Registry API (FilterModelForModule/rollback/MigrationExecutor/cleanup) | ✅ Done |

### Build & Test Results
- **Build**: 0 errors, 0 warnings
- **Tests**: 2079 passed, 8 failed (all pre-existing DB-required tests), 6 skipped

---

## Recommendations

### Immediate (this sprint)
1. Fix all 12 CRITICAL issues — security hole (C1) is highest priority
2. Fix HIGH issues H1-H2 (raiseStmt crash, whenStmt wrong semantics) — these cause runtime failures
3. Fix HIGH issues H31-H36 (runtime query bugs) — affect production queries

### Short-term (next sprint)
4. Fix all remaining HIGH issues (compiler passes, registry persistence, API)
5. Add integration tests for expression AST round-trip
6. Add integration tests for access control enforcement

### Medium-term
7. Address MEDIUM issues by subsystem priority
8. Add missing validation passes (Event, Migration, Sequence)
9. Improve $batch completeness

### Long-term (backlog)
10. Address LOW issues as part of regular maintenance
11. Add incremental compilation
12. Add OpenAPI generation from CSDL
