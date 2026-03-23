# Consolidated BMMDL Roadmap

> Generated: 2026-02-14
> Last updated: 2026-03-11 (ALL roadmap items COMPLETE, plugin architecture Phase 7 in progress)
> Sources: `docs/plan-close-language-feature-gaps.md`, `docs/design/entity-system-overhaul-plan.md`, `docs/design/bmmdl-events-design.md`

The 3 plans cover **3 major workstreams** with significant interdependencies:

- **Workstream 1**: Language Feature Gap Closure (`plan-close-language-feature-gaps.md`)
- **Workstream 2**: Entity System Overhaul (`entity-system-overhaul-plan.md`)
- **Workstream 3**: Event System + Transaction Safety (`bmmdl-events-design.md`)

---

## Dependency Graph

```
Events Phase 0 (UoW/Transactions) ─────────────────────────────┐
    │                                                            │
    ▼                                                            │
Events Phase 1 (emit/emits wiring) ◄── Gap C1 (callStmt rules) │
    │                                   Gap C2 (foreach rules)  │
    ▼                                   Gap C3 (let rules)      │
Events Phase 2 (service handlers)                                │
    │                                                            │
    ▼                                                            │
Events Phase 3-6 (outbox, retry, integration)                    │
                                                                 │
Entity Phase 0 (recursive aspects) ──────────────────────────────┤
    │                                                            │
    ▼                                                            │
Entity Phase 1 (behavioral aspects/AOP) ◄── Gap A1 (annotateDef)│
    │                                                            │
    ├──► Entity Phase 2 (extend entity)                          │
    │        │                                                   │
    │        ▼                                                   │
    ├──► Entity Phase 3 (modify entity)                          │
    │                                                            │
    └──► Entity Phase 4 (inheritance) ◄── Gap C7 (inheritance    │
              │                             controller wiring)   │
              ▼                                                  │
         Entity Phase 5 (cross-aspect views)                     │
                                                                 │
Gap C6 (ManyToMany $expand) ─── independent ─────────────────────┘
Gap B4 (callStmt args as expressions) ─── independent
Gap C5 (Array types) ─── independent
```

---

## Unified Priority Tiers

### Tier 0 — Architectural Foundation (must be done first)

| ID | Item | Source Doc | Effort | Impact |
|----|------|-----------|--------|--------|
| ~~**Events P0**~~ | ~~Unit of Work + Transaction Context~~ | events-design | Large | ~~CRITICAL — fixes data consistency bug across all CRUD~~ **DONE** — IUnitOfWork/UnitOfWork, ParameterizedQueryExecutor UoW-aware, all controllers wrapped, RuleEngine emit integration. 61 unit tests + 8 E2E tests. |
| ~~**Entity P0**~~ | ~~Fix recursive aspect chains~~ | entity-overhaul | Small | ~~Prerequisite for behavioral aspects~~ **DONE** — OptimizationPass recursive DFS chain resolution with cycle detection. 49 tests (21 compiler unit + 11 DDL codegen + 17 pipeline E2E). |

### Tier 1 — High-Impact Features (P1)

| ID | Item | Source Doc | Effort | Impact |
|----|------|-----------|--------|--------|
| **Gap C6** | ManyToMany `$expand` via junction table JOIN | gap-closure | Medium | HIGH — unblocks M:N relationship queries |
| **Gap C7** | Inheritance controller wiring (polymorphic GET, multi-table INSERT) | gap-closure | Medium | HIGH — DDL + SQL methods exist but aren't called |
| **Gap C1+C2+C3** | `callStmt`/`foreachStmt`/`letStmt` in entity rules | gap-closure | Small | MEDIUM — these work in actions but not rules |
| **Gap B4** | `callStmt` arguments as expressions (not strings) | gap-closure | Small | MEDIUM — type safety for action calls |
| ~~**Events P1**~~ | ~~Wire `emit`/`emits` runtime (RuleEngine + EntityActionController)~~ | events-design | Medium | ~~HIGH — makes event grammar features actually work~~ **DONE** — MetaModelCache.GetEvent(), EventSchemaValidator, emit/emits fully wired in RuleEngine + controllers. 65 tests. |

### Tier 2 — Feature Completeness

| ID | Item | Source Doc | Effort | Impact |
|----|------|-----------|--------|--------|
| ~~**Entity P1**~~ | ~~Behavioral Aspects (AOP) — grammar, MetaModel, OptimizationPass, RuleEngine `$old`+`reject`~~ | entity-overhaul | Large | ~~HIGH — transforms aspects from data-only to data+behavior~~ **DONE** — Grammar `rejectStmt`, BmRejectStatement, RuleEngine short-circuit reject, InlineAspectBehaviors (rules+ACLs from aspects), $old support. 46 tests. |
| ~~**Entity P2**~~ | ~~`extend entity` — ModelBuilder + ExtensionMergePass~~ | entity-overhaul | Medium | ~~MEDIUM — cross-module entity extension~~ **DONE** — Grammar `extendDef`, BmExtension model, ExtensionMergePass (entity/type/aspect), EXT_KEY_REDEFINITION validation. 49 tests. |
| ~~**Entity P4**~~ | ~~Entity Inheritance (table-per-type) — full pipeline~~ | entity-overhaul | Large | ~~HIGH — overlaps with Gap C7~~ **DONE** — subsumes Gap C7. |
| ~~**Gap C5**~~ | ~~Array types — TypeMappingRegistry + DDL + runtime~~ | gap-closure | Medium | ~~HIGH~~ **DONE** |
| ~~**Gap A1**~~ | ~~`annotateDef` — AnnotationMergePass~~ | gap-closure | Medium | ~~MEDIUM~~ **DONE** — Grammar `annotateDef`, BmAnnotateDirective, AnnotationMergePass (order 54) merges entity+field-level annotations. |
| ~~**Gap B1+B2**~~ | ~~Subquery/EXISTS expressions~~ | gap-closure | Medium | ~~MEDIUM~~ **DONE** — Grammar SubqueryExpr/ExistsExpr, BmSubqueryExpression/BmExistsExpression, PostgresSqlExpressionVisitor SQL generation. Runtime correctly throws NotSupportedException (SQL-only by design). |
| ~~**Gap A5**~~ | ~~Service `projectionClause`~~ | gap-closure | Small | ~~MEDIUM~~ **DONE** — Grammar projectionClause, IncludeFields/ExcludeFields in BmEntity, ODataMetadataController projection filtering. |
| ~~**Gap A7**~~ | ~~Action contracts (REQUIRES/ENSURES/MODIFIES)~~ | gap-closure | Small | ~~LOW~~ **DONE** — Runtime enforcement via InterpretedActionExecutor. |
| ~~**Events P2**~~ | ~~Service event handler grammar (`on EventName {}` in services)~~ | events-design | Small | ~~MEDIUM~~ **DONE** — Grammar `serviceEventHandler` rule, ModelBuilder VisitServiceEventHandler, pipeline compilation tests. 25 tests. |

### Tier 3 — Nice-to-Have / Future

| ID | Item | Source Doc | Effort | Impact |
|----|------|-----------|--------|--------|
| ~~**Entity P3**~~ | ~~`modify entity` — rename/remove/change type fields~~ | entity-overhaul | Medium | ~~HIGH risk (schema migration)~~ **DONE** — Key-field guard (MOD_CANNOT_REMOVE_KEY), FK-reference guard (MOD_FIELD_IN_USE), type-compatibility validation (MOD_TYPE_INCOMPATIBLE), reference-update on rename (rules, ACLs, computed fields), SchemaDiffer rename hints → RENAME COLUMN DDL instead of DROP+ADD. 68 tests. |
| ~~**Entity P5**~~ | ~~Cross-Aspect Views (UNION ALL)~~ | entity-overhaul | Small | ~~LOW — opt-in~~ **DONE** — OptimizationPass generates synthetic BmView entries for `@Query.CrossAspect` aspects, DDL generator creates PostgreSQL UNION ALL views, DynamicViewController can query them. 38 tests (15 compiler + 23 DDL). |
| ~~**Events P3**~~ | ~~Outbox pattern for durable events~~ | events-design | Medium | ~~MEDIUM~~ **DONE** — IOutboxStore, OutboxStore (Npgsql), OutboxProcessor background service, OutboxProcessorService, event_outbox DDL, DI registration. 17 tests. |
| ~~**Events P4**~~ | ~~Retry + dead letter queue~~ | events-design | Small | ~~MEDIUM~~ **DONE** — Included in Events P3 OutboxStore: exponential backoff retry (2^n seconds), dead_letter status on max retries exceeded, next_retry_at scheduling. |
| ~~**Events P5**~~ | ~~Observability + tracing (CausationId chain)~~ | events-design | Small | ~~LOW~~ **DONE** — DomainEvent EventId/CausationId/SchemaVersion/SourceModule fields, CorrelationId propagation from HttpContext, IEventMetrics interface + BmmdlMetrics (5 event counters), AuditLogStore causation_id column + event chain API, OutboxProcessor dead letter metrics. 39 tests. |
| ~~**Events P6**~~ | ~~Integration events (broker adapter)~~ | events-design | Large | ~~LOW for now~~ **DONE** — IBrokerAdapter interface + NullBrokerAdapter default, BmEvent.IsIntegration, IUnitOfWork.EnqueueDurableEvent → OutboxStore atomic write, OutboxProcessor routes @Integration events to broker adapter, RuleEngine emit routes by event type. 28 tests. |
| ~~**Gap C4**~~ | ~~Events runtime dispatch (MetaModelCache indexing)~~ | gap-closure | Large | ~~Covered by Events P1~~ **DONE** — Subsumed by Events P1 (MetaModelCache.GetEvent, EventSchemaValidator). |
| ~~**Gap A2**~~ | ~~`migrationDef` (UP/DOWN blocks)~~ | gap-closure | Large | ~~MEDIUM~~ **DONE** |
| ~~**Gap A4**~~ | ~~`projectionDef` in views~~ | gap-closure | Small | ~~LOW~~ **DONE** |
| ~~**Gap B3**~~ | ~~Temporal interval operators~~ | gap-closure | Small | ~~LOW~~ **DONE** |
| ~~**Gap C8**~~ | ~~HasStream entity media~~ | gap-closure | Medium | ~~LOW~~ **DONE** |
| ~~**Gap C9**~~ | ~~View SELECT AST parsing~~ | gap-closure | Large | ~~LOW~~ **DONE** |

---

## Overlap / Deduplication Notes

1. **Gap C7 <-> Entity P4**: Both address entity inheritance at runtime. Entity P4 is the comprehensive plan; Gap C7 is just "wire existing SQL methods to controller." If doing Entity P4, Gap C7 is subsumed.

2. **Gap C4 <-> Events P1**: Both address event dispatch. Events P1 is the proper design; Gap C4 (add `_eventsByName` to MetaModelCache) is a subset of it.

3. **Gap C1/C2/C3 <-> Events P1**: The rule engine statement gaps (call/foreach/let) should be done together with emit wiring since they're all in `RuleEngine.ExecuteStatementAsync`.

4. **Gap A1 <-> Entity P1**: `annotateDef` (AnnotationMergePass) has already been partially implemented (per git log: `feat: add AnnotationMergePass`). Behavioral aspects build on top of annotation infrastructure.

---

## Suggested Execution Order

```
 1. Events P0 (UoW/Transactions)     <- DONE ✓ (fixes data safety, enables everything)
 2. Entity P0 (recursive aspects)     <- DONE ✓ (recursive DFS + cycle detection, 49 tests)
 3. Gap C6 (M:N $expand)              <- DONE ✓ (independent, high impact)
 4. Gap C1+C2+C3+B4 (rule statements) <- DONE ✓ (small, quick wins)
 5. Events P1 (emit/emits wiring)     <- DONE ✓ (GetEvent, EventSchemaValidator, 65 tests)
 6. Entity P1 (behavioral aspects)    <- DONE ✓ (reject stmt, behavioral inlining, 46 tests)
 7. Entity P4 (inheritance)           <- DONE ✓ (subsumes Gap C7)
 8. Entity P2 (extend entity)         <- DONE ✓ (ExtensionMergePass, key validation, 49 tests)
 9. Gap C5 (array types)              <- DONE ✓ (independent)
10. Events P2-3 (service handlers, outbox) <- DONE ✓ (grammar + outbox, 42 tests)
11. Events P5 (observability + tracing)   <- DONE ✓ (CausationId chain, event metrics, 39 tests)
12. Entity P5 (cross-aspect views)        <- DONE ✓ (synthetic BmView + DDL + queryable, 38 tests)
13. Entity P3 (modify entity)             <- DONE ✓ (safety guards, rename propagation, 68 tests)
14. Events P6 (integration events)        <- DONE ✓ (broker adapter, durable events, 28 tests)
15. ALL ROADMAP ITEMS COMPLETE ✓
```

**Total scope**: ~40 distinct work items across 3 workstreams. Events P0 (UoW) — the most impactful architectural change — is now **DONE**, fixing the fundamental data consistency issue across all CRUD operations.

---

## Files Impact Summary

### Most Modified Files (across all plans)

| File | Touched By |
|------|-----------|
| `src/BMMDL.Runtime/Rules/RuleEngine.cs` | Gap C1-C3, Events P1, Entity P1 |
| `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` | Gap C6, Entity P4, Events P0 |
| `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` | Gap C7, Entity P1/P4, Events P0 |
| `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs` | Gap B4, Entity P1-P4 |
| `src/BMMDL.MetaModel/BmModel.cs` | Gap B4, Entity P1-P3, Events P1 |
| `src/BMMDL.Runtime/MetaModelCache.cs` | Entity P4, Events P1 |
| `src/BMMDL.Runtime/DataAccess/ParameterizedQueryExecutor.cs` | Events P0 |
| `src/BMMDL.Compiler/Pipeline/Passes/OptimizationPass.cs` | Entity P0, P1 |
| `Grammar/BmmdlParser.g4` | Entity P1, P4, Events P2 |

### New Files Required

| File | Plan |
|------|------|
| `src/BMMDL.Runtime/DataAccess/IUnitOfWork.cs` | Events P0 |
| `src/BMMDL.Runtime/DataAccess/UnitOfWork.cs` | Events P0 |
| `src/BMMDL.Runtime/Events/OutboxStore.cs` | Events P3 |
| `src/BMMDL.Runtime/Events/OutboxProcessor.cs` | Events P3 |
| `src/BMMDL.Compiler/Pipeline/Passes/InheritanceResolutionPass.cs` | Entity P4 |
| `src/BMMDL.Compiler/Pipeline/Passes/ExtensionMergePass.cs` | Entity P2 |
| `src/BMMDL.Compiler/Pipeline/Passes/ModificationPass.cs` | Entity P3 |
