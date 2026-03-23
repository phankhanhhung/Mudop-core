# Plan: Close BMMDL Language Feature Gaps

> Last reviewed: 2026-02-15

## Context
Full audit of Grammar → Compiler → Runtime revealed features defined in the ANTLR grammar that are either not compiled (no ModelBuilder visitor) or compiled but not available at runtime. This plan catalogs every gap and proposes implementation for each.

**False positives eliminated (verified implemented):**
- Extensions/Modifications — applied in-place during compilation (ExtensionMergePass, ModificationPass)
- Aspect Rules/ACLs — inlined by OptimizationPass
- Views — `DynamicViewController` provides full OData support ($filter, $select, $orderby, $top, $skip, temporal)
- Sequences — full support (lazy reset via `get_next_sequence_value()` SQL function on INSERT)
- `annotateDef` — `AnnotationMergePass` (order 54) fully implements annotation injection
- Field-level access control — `FieldRestrictionApplier` enforces VISIBLE WHEN/MASKED/READONLY/HIDDEN in all controllers
- STORED computed field stripping — `StripComputedFields()` handles on create/update/bulk import
- Database action/function deployment — `PgSqlActionGenerator` (565 lines) + `DatabaseActionExecutor` (197 lines) + `HybridActionExecutor` (89 lines)
- Domain events — `EventPublisher` + `ServiceEventHandler` + `AuditLogEventHandler` + auto-publish on CRUD

---

## ~~P1 — ALL COMPLETE (verified 2026-02-15)~~

### ~~B4. `callStmt` args as expressions — DONE~~
~~`BmCallStatement.Arguments` is `List<BmExpression>` (not strings). `BmExpressionBuilder` parses arguments correctly.~~

### ~~C1. `callStmt` in entity rules — DONE~~
~~`RuleEngine.ExecuteCallStatementAsync` (line 377) resolves bound/unbound actions and executes their body with child context. Unit tests: 3 tests in `RuleEngineTests`.~~

### ~~C2. `foreachStmt` in entity rules — DONE~~
~~`RuleEngine.ExecuteForeachStatementAsync` (line 422) evaluates collection, iterates with loop variable in child context. Unit tests: 3 tests in `RuleEngineTests`.~~

### ~~C3. `letStmt` in entity rules — DONE~~
~~`RuleEngine.ExecuteLetStatement` (line 458) evaluates expression AST and stores in `context.Parameters`. Unit tests: 2 tests in `RuleEngineTests`.~~

### ~~C6. ManyToMany `$expand` — DONE~~
~~Full implementation: `ExpandManyToManyProperty` (single, line 1813) + `ExpandOneToManyPropertiesBatched` (batched M:M with junction JOIN, line 1883) + `FindOneToManyAssociation` includes M:M. `$ref` link/unlink via `EntityReferenceController`. Cascade junction cleanup on delete. E2E tests: 4 tests in `ManyToManyE2ETests`.~~

### ~~C7. Inheritance controller wiring — DONE~~
~~All wired in `DynamicEntityController`: `BuildPolymorphicSelectQuery` in GetAll (line 250), `BuildInheritanceSelectQuery` in GetById (line 427), `BuildInheritanceInsertQueries` in Create (line 640), `BuildInheritanceUpdateQueries` in Update (line 753), `BuildInheritanceDeleteQueries` in Delete (line 1210). Abstract entity guard on Create (line 562). E2E tests: 6 tests in `InheritanceE2ETests`.~~

---

## A. Grammar Features Not Compiled (no ModelBuilder visitor)

### A2. `migrationDef` — Explicit migration scripts
**Grammar:** `Grammar/BmmdlParser.g4:784-853`
```
migration "v1.1 add email" { up { ... } down { ... } }
```
**Missing:** No visitor, no `BmMigration` class.
**Use case:** Custom data transformations, manual schema steps beyond auto-migration.
**Plan:**
- Add `BmMigrationDef` class (Name, UpSteps, DownSteps) to `BmModel.cs`
- Add `List<BmMigrationDef> MigrationDefs` to `BmModel`
- Add `VisitMigrationDef()` in `BmmdlModelBuilder.cs`
- Wire into `MigrationScriptGenerator` — embed explicit steps alongside auto-generated ones

### A3. `contextDef` — Logical grouping
**Grammar:** `Grammar/BmmdlParser.g4:106-108`
```
context MyContext { entity Foo { ... } }
```
**Missing:** No visitor. Namespace blocks provide similar functionality.
**Plan:** LOW PRIORITY — skip or alias to namespace. Minimal value.

### A4. `projectionDef` in views/tables
**Grammar:** `Grammar/BmmdlParser.g4:283-290`
```
projection on Entity { field1; field2; * excluding (field3); }
```
**Missing:** `VisitTableDef` only handles `selectStatement`, skips `projectionDef` branch.
**Plan:**
- In `VisitTableDef()`: if `context.projectionDef() != null`, convert projection syntax to equivalent SELECT statement string
- Store as `BmView.SelectStatement` (same as SELECT path)

### ~~A5. Service `projectionClause` — DONE~~
~~**Missing:** Grammar defined but not visited — `VisitEntityExposure` ignores projection.~~
**Verified:** `projectionClause` parsing fully implemented in `BmmdlModelBuilder.cs` (lines 1242-1265). Stored in `BmEntity.IncludeFields`/`ExcludeFields` (lines 174-188). Applied to CSDL and JSON metadata in `ODataMetadataController.GetServiceProjection` (lines 247-262). E2E: FleetService `Items as TaggedItem { * excluding { metadata } }` works.

### A6. `temporalQualifier` in SELECT
**Grammar:** `Grammar/BmmdlParser.g4:325-330`
```
SELECT * FROM Entity TEMPORAL ASOF '2024-01-01'
```
**Missing:** Not parsed in view SELECT statements.
**Plan:** LOW PRIORITY — runtime supports `asOf` via query params. DSL temporal qualifiers only matter for compiled views.

### ~~A7. Action contract clauses (`REQUIRES`, `ENSURES`, `MODIFIES`) — DONE~~
~~**Status:** Parsed and stored in `BmAction.Preconditions`/`Postconditions` — but **never evaluated** at runtime.~~
**Implemented:** `InterpretedActionExecutor.ExecuteActionAsync` evaluates preconditions (REQUIRES) before body execution and postconditions (ENSURES) after body. `HybridActionExecutor` enforces contracts around DB execution path. New exception types: `PreconditionFailedException`, `PostconditionFailedException`. Unit tests: 8 tests in `InterpretedActionExecutorTests`. E2E tests: 3 tests in `ActionContractE2ETests` (SubmitInvoice with requires status='draft' + subtotal>0, ensures status='submitted').

---

## B. Expressions Not Compiled (no ExpressionBuilder visitor)

### ~~B1. `SubqueryExpr` — DONE~~
~~**Missing:** No visitor, no `BmSubqueryExpression` AST class.~~
**Verified:** `BmSubqueryExpression` AST class exists (line 555 of `BmExpression.cs`), `VisitSubqueryExpr` in `BmExpressionBuilder.cs` (line 98), SQL generation in `PostgresSqlExpressionVisitor`. Runtime correctly throws `NotSupportedException` (SQL-only by design).

### ~~B2. `ExistsExpr` — DONE~~
~~**Missing:** No visitor, no AST class.~~
**Verified:** `BmExistsExpression` AST class exists (line 576 of `BmExpression.cs`), `VisitExistsExpr` in `BmExpressionBuilder.cs` (line 106), SQL generation complete. Runtime correctly throws `NotSupportedException` (SQL-only by design).

### B3. Temporal interval operators (`OverlapsExpr`, `ContainsExpr`, `PrecedesExpr`, `MeetsExpr`)
**Grammar:** `Grammar/BmmdlParser.g4:702-705`
**Missing:** No visitors, no AST classes.
**Plan:** LOW PRIORITY — niche temporal use case. Add `BmTemporalBinaryExpression` with operator enum.

---

## C. Compiled Features Not Available at Runtime

### ~~C4. Events — DONE~~
~~Core event framework complete. Only remaining micro-gap: `BmCallStatement` within `ServiceEventHandler` event handlers.~~

### ~~C5. Array types — DONE~~
~~**Missing:** No mapping in `TypeMappingRegistry`, no DDL, no runtime CRUD~~
**Verified:** Full implementation exists. `TypeResolver.ResolveArrayType` (lines 254-281) generates PostgreSQL array columns. `DynamicSqlBuilder.ConvertArrayValue` (line 1671) handles array params. INSERT/UPDATE/GET all work. 4 E2E tests in `ArrayTypeE2ETests`.

### C8. Entity-level media streams (`@HasStream`) — DDL only
**DDL:** Generates `_media_content`, `_media_content_type`, `_media_etag` columns
**Runtime:** No upload/download controller for entity-level streams
**Plan:** LOW PRIORITY — property-level `FileReference` covers most needs. Defer.

### C9. View `selectStatement` stored as raw string
**File:** `BmmdlModelBuilder.cs` — `BmView.SelectStatement = context.selectStatement().GetText()`
**Impact:** No validation, no dependency analysis, no SQL optimization for views
**Plan:** MEDIUM PRIORITY — parse into AST for dependency tracking. Complex; defer to separate effort.

---

## Priority Matrix

| ID | Feature | Impact | Effort | Priority |
|----|---------|--------|--------|----------|
| ~~C5~~ | ~~Array types~~ | ~~HIGH~~ | ~~Medium~~ | ~~**P2**~~ **DONE** |
| ~~B1~~ | ~~`SubqueryExpr`~~ | ~~MEDIUM~~ | ~~Medium~~ | ~~**P2**~~ **DONE** |
| ~~B2~~ | ~~`ExistsExpr`~~ | ~~MEDIUM~~ | ~~Medium~~ | ~~**P2**~~ **DONE** |
| ~~A5~~ | ~~Service `projectionClause`~~ | ~~MEDIUM~~ | ~~Small~~ | ~~**P2**~~ **DONE** |
| ~~A7~~ | ~~Action contracts (runtime enforcement)~~ | ~~MEDIUM~~ | ~~Small~~ | ~~**P2**~~ **DONE** |
| ~~A2~~ | ~~`migrationDef`~~ | ~~MEDIUM~~ | ~~Large~~ | ~~**P3**~~ **DONE** |
| ~~A4~~ | ~~`projectionDef` in views~~ | ~~LOW~~ | ~~Small~~ | ~~**P3**~~ **DONE** |
| ~~C9~~ | ~~View SELECT AST parsing~~ | ~~LOW~~ | ~~Large~~ | ~~**P3**~~ **DONE** |
| ~~C8~~ | ~~HasStream entity media~~ | ~~LOW~~ | ~~Medium~~ | ~~**P3**~~ **DONE** |
| ~~B3~~ | ~~Temporal operators~~ | ~~LOW~~ | ~~Small~~ | ~~**P3**~~ **DONE** |
| A3 | `contextDef` | LOW | Small | **Skip** |
| A6 | `temporalQualifier` in SELECT | LOW | Small | **Skip** |

**Completed:** A1 (`annotateDef`), A2 (`migrationDef`), A4 (`projectionDef` views), A5 (service projections), A7 (action contracts), B1 (`SubqueryExpr`), B2 (`ExistsExpr`), B3 (temporal operators), B4 (`callStmt` args), C1 (`callStmt` in rules), C2 (`foreachStmt` in rules), C3 (`letStmt` in rules), C4 (domain events + service calls), C5 (array types), C6 (ManyToMany `$expand`), C7 (Inheritance wiring), C8 (HasStream media), C9 (View SELECT AST), field-level ACL, view queries, stored computed stripping, DB action deployment. Also: abstract entity enforcement, aggregate expressions in rules, 13 missing built-in functions, 7 compiler validation gaps (20 tests).

## ~~Files to Modify (P2 items)~~ — ALL P2 COMPLETE

All P2 items verified as implemented. Only A7 required new code (action contract runtime enforcement). Others (C5, B1, B2, A5) were already fully implemented.

**Files Modified for A7:**

| File | Changes |
|------|---------|
| `src/BMMDL.Runtime/Services/InterpretedActionExecutor.cs` | Added precondition/postcondition evaluation in `ExecuteActionAsync`, new `PreconditionFailedException`/`PostconditionFailedException` types |
| `src/BMMDL.Runtime/Services/HybridActionExecutor.cs` | Added `RuntimeExpressionEvaluator` field, pre/post contract checks around DB execution path |
| `erp_modules/11_warehouse/test_advanced_features.bmmdl` | Added `SubmitInvoice` action with `requires`/`ensures` contracts |
| `src/BMMDL.Tests/Runtime/Services/InterpretedActionExecutorTests.cs` | 8 new unit tests for contract enforcement |
| `src/BMMDL.Tests.New/Advanced/ActionContractE2ETests.cs` | 3 new E2E tests |

## Verification

1. `dotnet build BMMDL.sln` — 0 errors
2. `dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj` — all 1343 unit tests pass (1337 passed, 6 skipped)
3. `dotnet test src/BMMDL.Tests.New/BMMDL.Tests.New.csproj --filter "ManyToManyE2ETests|InheritanceE2ETests|RuleEngineAdvancedE2ETests|ActionContractE2ETests|ArrayTypeE2ETests"` — all 21 E2E tests pass
4. ManyToMany: $expand returns linked entities through junction table, $ref link/unlink works, cascade cleanup on delete
5. Inheritance: polymorphic GET returns Car+Truck with discriminator, multi-table INSERT/UPDATE/DELETE, abstract entity guard rejects direct create
6. Rule statements: compute/validate/let/call/foreach/when all execute in entity rules
7. Action contracts: preconditions reject invalid calls (status check, subtotal check), postconditions validate results
8. Array types: CREATE/GET/UPDATE with Array<String> and Array<Integer> columns
9. Subquery/EXISTS: full AST, visitor, SQL generation (SQL-only by design)
10. Service projections: field exclusion applied to CSDL metadata
