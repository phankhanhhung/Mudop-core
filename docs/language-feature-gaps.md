# BMMDL Language Feature Gap Analysis

> Generated: 2026-02-14
> Last reviewed: 2026-02-15 (P3 batch complete)
> Scope: Grammar → Compiler → CodeGen → Runtime (end-to-end)

This document catalogs all BMMDL language features defined in the grammar (`Grammar/BmmdlLexer.g4`, `Grammar/BmmdlParser.g4`) and their implementation status across the full pipeline.

### Legend
- **P** = Parsed by grammar
- **C** = Compiled (model builder + passes)
- **D** = DDL generated
- **R** = Runtime executable

---

## 1. Critical Gaps (Grammar defined, minimal/no implementation)

| Feature | P | C | D | R | Notes |
|---------|---|---|---|---|-------|
| `context` definitions | Y | N | N | N | Grammar parses `context Name { ... }` but no `BmContext` class exists. Silently discarded. |
| ~~`migration` definitions~~ | Y | **Y** | **Y** | N/A | ~~Full migration grammar (UP/DOWN, ALTER, TRANSFORM) exists but model builder ignores it entirely.~~ **DONE** — `BmMigrationDef` model, `VisitMigrationDef` visitor, `MigrationScriptGenerator` integration. 22 unit tests. |
| ~~Action preconditions/postconditions~~ | Y | Y | N | **Y** | ~~`REQUIRES`/`ENSURES` clauses parsed into model but never evaluated during action execution.~~ **DONE** — `InterpretedActionExecutor` evaluates preconditions before body, postconditions after. `HybridActionExecutor` enforces contracts around DB execution. 8 unit tests + 3 E2E tests. |

---

## 2. Significant Gaps (Partially implemented)

| Feature | P | C | D | R | Notes |
|---------|---|---|---|---|-------|
| Sequences | Y | Y | Y | **Partial** | DDL infrastructure exists (core.__sequences table, SQL functions). Reset logic is embedded in `get_next_sequence_value()` (lazy on INSERT), but no background scheduler for proactive DAILY/MONTHLY/YEARLY resets. |
| ~~Temporal expressions~~ | Y | Y | **Y** | **Y** | ~~`OVERLAPS`, `CONTAINS`, `PRECEDES`, `MEETS` operators in grammar but `RuntimeExpressionEvaluator` doesn't handle them.~~ **DONE** — `BmTemporalBinaryExpression` AST, visitor methods, `PostgresSqlExpressionVisitor`, `RuntimeExpressionEvaluator`. 18 unit tests. |
| ~~Subquery/EXISTS expressions~~ | Y | Y | Y | **SQL-only** | ~~Explicitly throw `NotSupportedException` in runtime evaluator.~~ **DONE** — Full AST classes (`BmSubqueryExpression`, `BmExistsExpression`), parser visitors, and SQL generation. Runtime correctly throws `NotSupportedException` (by design — these are SQL-context features). |
| ~~Aggregate expressions in rules~~ | Y | Y | N/A | **Y** | ~~`COUNT`, `SUM`, etc. throw `NotSupportedException` in `RuntimeExpressionEvaluator`.~~ **DONE** — `AggregateExpressionResolver` builds parameterized subqueries with tenant isolation. 19 unit tests. |
| ~~Built-in functions~~ | Y | Y | Y | **Y** | ~~53 of 63 grammar-defined functions implemented (84% coverage).~~ **DONE** — Added 13 functions: STDDEV, VARIANCE, INSTR, DECODE, IFNULL, TO_INTEGER/DECIMAL/DATE/STRING/TIME/TIMESTAMP, CURRENCY_CONVERSION (stub), UNIT_CONVERSION (stub), LPAD/RPAD aliases. ~40 unit tests. Only window functions remain (no parser rules). |
| Expression path navigation | Y | **Partial** | N | N | `Order.Customer.Name` style paths parsed but `BindingPass` explicitly notes cross-entity path resolution not implemented (TODO placeholder at line 160). |
| ~~Abstract entities~~ | Y | Y | **Y** | **Y** | ~~`IsAbstract` flag exists but DDL still generates tables and runtime still allows direct queries.~~ **DONE** — DDL skips abstract entities without derived entities. OData service document excludes abstract entities. CSDL marks `Abstract="true"`. Create guard already existed. 4 unit tests. |
| ~~Domain events — service-to-service calls~~ | Y | Y | N/A | **Y** | ~~Core event framework complete. Only remaining gap: `BmCallStatement` within event handlers.~~ **DONE** — `ExecuteCallStatementAsync` in `ServiceEventHandler` resolves 3 target formats (simple, entity-bound, service-qualified). Circular call detection via `AsyncLocal`. Also added when/let/foreach/raise support. 11 unit tests. |

---

## ~~3. Compiler Validation Gaps~~ — ALL DONE

All 7 validation gaps fixed with 20 new unit tests. Error codes SEM060-SEM066 added.

| Feature | Gap | Status |
|---------|-----|--------|
| ~~Type base type resolution~~ | ~~base type never validated~~ | **DONE** — `ResolveTypeBaseTypes` in SymbolResolutionPass (SEM066) |
| ~~Enum value uniqueness~~ | ~~Duplicate enum values allowed~~ | **DONE** — `ValidateEnumValueUniqueness` in SemanticValidationPass (SEM060) |
| ~~Cardinality enforcement~~ | ~~bounds not validated~~ | **DONE** — `ValidateCardinality` min≤max, min≥0 (SEM062, WARNING) |
| ~~Index column validation~~ | ~~columns not checked~~ | **DONE** — `ValidateIndexColumns` checks fields+associations+compositions (SEM061) |
| ~~Constraint expression validation~~ | ~~not semantically checked~~ | **DONE** — `ValidateConstraintFields` walks AST for identifier refs (SEM064, WARNING) |
| ~~Function/action parameter types~~ | ~~types not resolved~~ | **DONE** — `ResolveServiceOperationTypes` in SymbolResolutionPass (SEM065, WARNING) |
| ~~Default expression type checking~~ | ~~not validated against field type~~ | **DONE** — `ValidateDefaultTypes` literal type compatibility (SEM063, WARNING) |

---

## 4. Features Fully Working (End-to-end)

| Feature | P | C | D | R |
|---------|---|---|---|---|
| Entity definitions + fields | Y | Y | Y | Y |
| Entity inheritance (table-per-type, polymorphic GET, multi-table CRUD) | Y | Y | Y | Y |
| Associations (FK columns) | Y | Y | Y | Y |
| Compositions (parent FK) | Y | Y | Y | Y |
| Many-to-many junction tables + $expand + $ref | Y | Y | Y | Y |
| Enums (VARCHAR + CHECK) | Y | Y | Y | Y |
| Type aliases | Y | Y | Y | Y |
| Aspects (field inlining) | Y | Y | Y | Y |
| Annotations (core set) | Y | Y | Y | Y |
| Business rules (validate/compute/when/call/let/foreach) | Y | Y | N/A | Y |
| Access control (GRANT/DENY entity-level) | Y | Y | N/A | Y |
| Field-level access control (VISIBLE WHEN/MASKED/READONLY/HIDDEN) | Y | Y | N/A | Y |
| Computed fields (virtual/stored DDL) | Y | Y | Y | Y |
| STORED computed field stripping on insert/update | N/A | N/A | N/A | Y |
| Localized fields (_texts table) | Y | Y | Y | Y |
| Temporal entities (inline/separate) | Y | Y | Y | Y |
| Array types | Y | Y | Y | Y |
| FileReference (8 metadata cols) | Y | Y | Y | Y |
| Tenant isolation (RLS) | Y | Y | Y | Y |
| Indexes & unique constraints | Y | Y | Y | Y |
| Services (entity exposure + projection clauses) | Y | Y | N/A | Y |
| Subquery/EXISTS expressions (SQL context) | Y | Y | Y | Y |
| Bound actions/functions (with REQUIRES/ENSURES contract enforcement) | Y | Y | N/A | Y |
| Extend/Modify/Annotate directives | Y | Y | N/A | N/A |
| View/table runtime queries (DynamicViewController) | Y | Y | Y | Y |
| Domain events (EventPublisher + ServiceEventHandler + AuditLog) | Y | Y | N/A | Y |
| Database action/function deployment (PgSqlActionGenerator + HybridActionExecutor) | Y | Y | Y | Y |
| OData CRUD + query options | N/A | N/A | N/A | Y |
| Deep insert/update | N/A | N/A | N/A | Y |
| $batch, $ref, $expand+$levels | N/A | N/A | N/A | Y |
| ETag/If-Match concurrency | N/A | N/A | N/A | Y |
| Delta responses ($deltatoken) | N/A | N/A | N/A | Y |
| Singleton entities (@OData.Singleton) | N/A | N/A | N/A | Y |
| Capability annotations in CSDL | N/A | N/A | N/A | Y |
| Prefer header (return=minimal, maxpagesize) | N/A | N/A | N/A | Y |
| Computed/ReadOnly field stripping | N/A | N/A | N/A | Y |

---

## 5. Dead Code in Grammar

These lexer tokens exist but have **no parser rules** referencing them:

| Token | Purpose | Status |
|-------|---------|--------|
| `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE` | Window functions | Tokens only, no parser rule |
| `LAG`, `LEAD`, `FIRST_VALUE`, `LAST_VALUE` | Window offset functions | Tokens only, no parser rule |
| `OVER`, `PARTITION`, `ROWS`, `RANGE` | Window frame specification | Tokens only, no parser rule |
| `UNBOUNDED`, `PRECEDING`, `FOLLOWING` | Window frame bounds | Tokens only, no parser rule |
| `SEALED` | Reserved for future use | Not referenced in any rule |

Note: `FISCAL` is used in the `resetTrigger` parser rule (`FISCAL YEAR?`).

---

## 6. Priority Recommendations

### High Priority (correctness)
1. ~~**Action REQUIRES/ENSURES** — preconditions should reject invalid calls; postconditions should validate results~~ **DONE**
2. ~~**Abstract entity enforcement** — skip DDL for abstract entities (unless inheritance root), exclude from service document~~ **DONE**

### Medium Priority (feature completeness)
3. ~~**Aggregates in rules** — enable `validate count(orders) > 0` in business rules~~ **DONE**
4. ~~**Migration execution** — implement UP/DOWN block processing for schema versioning~~ **DONE**
5. ~~**Service-to-service calls in event handlers** — complete `BmCallStatement` in `ServiceEventHandler`~~ **DONE**
6. ~~**Missing built-in functions** — `CURRENCY_CONVERSION`, `UNIT_CONVERSION`, `STDDEV`, `VARIANCE`, type conversions~~ **DONE**

### Low Priority (nice-to-have)
7. **Context definitions** — decide semantics or remove from grammar
8. ~~**Temporal operators in runtime** — `OVERLAPS`/`PRECEDES` for rule conditions~~ **DONE**
9. ~~**Compiler validation improvements** — enum uniqueness, cardinality bounds, type checking~~ **DONE** (7 validations, 20 tests)
10. **Subquery/EXISTS in rules** — complex validation patterns (SQL-only by design)
11. **Expression path navigation** — cross-entity path resolution in BindingPass
12. **Sequence background resets** — proactive scheduler for DAILY/MONTHLY/YEARLY triggers
