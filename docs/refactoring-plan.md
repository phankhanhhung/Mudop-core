# BMMDL Backend Refactoring Plan

**Based on deep code review of 80+ files, ~130 issues identified**

---

## Phase Overview

| Phase | Name | Impact | Files Changed | Estimated Dedup |
|-------|------|--------|---------------|-----------------|
| R1 | Extract StatementExecutor + CallTargetResolver | HIGH | 6 new, 3 modified | ~500 lines |
| R2 | Extract RowReader + PlatformServiceBase | HIGH | 2 new, 4 modified | ~200 lines |
| R3 | Consolidate EntityWriteService shared methods | HIGH | 1 modified | ~300 lines |
| R4 | Extract DeepOperationBase for handlers | HIGH | 1 new, 2 modified | ~250 lines |
| R5 | Centralize constants | MEDIUM | 3 new, 20+ modified | maintainability |
| R6 | Extract SqlTypeMapper | MEDIUM | 1 new, 3 modified | ~150 lines |
| R7 | Create BmExpressionWalker | MEDIUM | 1 new, 4 modified | ~200 lines |
| R8 | Consolidate compiler element parsing | MEDIUM | 1 new, 2 modified | ~300 lines |
| R9 | Reintroduce interfaces for key services | MEDIUM | 5 new, 8 modified | testability |
| R10 | Controller responsibility extraction | MEDIUM | 3 new, 6 modified | ~400 lines |

---

## R1: Extract StatementExecutor + CallTargetResolver

**Problem**: Statement execution dispatch (10+ BmRuleStatement subtypes) is independently implemented in 3 classes with near-identical switch statements. Call target resolution is also triplicated.

**Files to create**:
- `src/BMMDL.Runtime/Rules/IStatementExecutor.cs` — interface
- `src/BMMDL.Runtime/Rules/StatementExecutor.cs` — shared implementation with configurable error policy
- `src/BMMDL.Runtime/Rules/IStatementExecutionPolicy.cs` — strategy interface (fail-fast vs resilient)
- `src/BMMDL.Runtime/Rules/FailFastPolicy.cs` — for rules
- `src/BMMDL.Runtime/Rules/ResilientPolicy.cs` — for event handlers
- `src/BMMDL.Runtime/Services/CallTargetResolver.cs` — extracted from 3 files

**Files to modify**:
- `src/BMMDL.Runtime/Rules/RuleStatementExecutor.cs` — delegate to StatementExecutor
- `src/BMMDL.Runtime/Services/InterpretedActionExecutor.cs` — delegate to StatementExecutor
- `src/BMMDL.Runtime/Events/ServiceEventHandler.cs` — delegate to StatementExecutor

**Also consolidates**:
- `ConvertToBool()` wrappers (6 files) → call `TypeConversionHelpers.ConvertToBool()` directly
- `CreateChildEvalContext()` (4 files) → add `EvaluationContext.CreateChild()` instance method
- Emit execution (2 files) → move into StatementExecutor

**Dependencies**: None

---

## R2: Extract RowReader + PlatformServiceBase

**Problem**: `ReadRow(NpgsqlDataReader)` duplicated in 4 files. `CreateCommand()`/`ExecuteSingleAsync()`/`ExecuteListAsync()` duplicated in 2 platform services.

**Files to create**:
- `src/BMMDL.Runtime/DataAccess/RowReader.cs` — static utility for reading NpgsqlDataReader to Dictionary
- `src/BMMDL.Runtime/Services/PlatformServiceBase.cs` — abstract base with shared data access methods

**Files to modify**:
- `src/BMMDL.Runtime/DataAccess/ParameterizedQueryExecutor.cs` — use RowReader
- `src/BMMDL.Runtime/DynamicRepository.cs` — use RowReader
- `src/BMMDL.Runtime/Services/DynamicPlatformUserService.cs` — extend PlatformServiceBase
- `src/BMMDL.Runtime/Services/DynamicPlatformTenantService.cs` — extend PlatformServiceBase

**Dependencies**: None

---

## R3: Consolidate EntityWriteService Shared Methods

**Problem**: `CreateAsync` (148 lines), `UpdateAsync` (188 lines), `ReplaceAsync` (139 lines) share 80% identical patterns: strip computed → validate enum → validate JSONB → HasStream → FK → rules → localization → events. ETag validation duplicated 3x. Localization duplicated 2x. Event enqueue duplicated 4x.

**Extract into private methods within the same file**:
- `ValidateInputData(entityDef, data, isUpdate)` — strip computed, validate enum, validate JSONB, HasStream, FK
- `StoreLocalizedFields(entityDef, id, data, locale, tenantId, ct)`
- `EnqueueDomainEvent(eventName, entity, id, payload, context)`
- `ValidateETag(currentRecord, ifMatch)` — returns PreconditionFailed or null
- `PerformUpdateOrReplace(entityDef, id, data, isReplace, ...)` — shared update/replace core

**Files to modify**:
- `src/BMMDL.Runtime.Api/Services/EntityWriteService.cs`

**Dependencies**: None

---

## R4: Extract DeepOperationBase for Handlers

**Problem**: DeepInsertHandler and DeepUpdateHandler share: rule execution patterns (before/after), `GetForeignKeyFieldName()`, `CreateChildEvalContext()`, FK validation logic. `ProcessNestedCollectionAsync` in DeepUpdateHandler is 225 lines.

**Files to create**:
- `src/BMMDL.Runtime.Api/Handlers/DeepOperationBase.cs` — shared base class

**Shared methods to extract**:
- `ExecuteNestedWithRulesAsync(entityDef, data, tenantId, evalContext)` — before rules → compute → insert/update → after rules
- `GetForeignKeyFieldName(association, parentEntity)` — identical in both
- `CreateChildEvalContext(parentContext, childData)` — identical in both (also covered by R1's EvaluationContext.CreateChild)

**Files to modify**:
- `src/BMMDL.Runtime.Api/Handlers/DeepInsertHandler.cs` — extend DeepOperationBase
- `src/BMMDL.Runtime.Api/Handlers/DeepUpdateHandler.cs` — extend DeepOperationBase, split `ProcessNestedCollectionAsync` into `UpdateChildWithRules`, `CreateChildWithRules`, `DeleteOrphansWithRules`

**Dependencies**: R1 (for EvaluationContext.CreateChild)

---

## R5: Centralize Constants

**Problem**: Hard-coded strings scattered across 20+ files for entity names, table names, OData error codes, query options, column names, compiler error codes.

**Files to create**:
- `src/BMMDL.Runtime/Constants/PlatformEntityNames.cs`
  ```csharp
  public static class PlatformEntityNames
  {
      public const string User = "Core.User";
      public const string Role = "Core.Role";
      public const string UserRole = "Core.UserRole";
      public const string Tenant = "Platform.Tenant";
      public const string Identity = "Platform.Identity";
  }
  ```
- `src/BMMDL.Runtime/Constants/ODataConstants.cs`
  ```csharp
  public static class ODataConstants
  {
      // Error codes
      public const string EntityNotFound = "ENTITY_NOT_FOUND";
      public const string ValidationError = "VALIDATION_ERROR";
      public const string ActionExecutionFailed = "ACTION_EXECUTION_FAILED";
      // Query options
      public const string Filter = "$filter";
      public const string OrderBy = "$orderby";
      public const string Expand = "$expand";
      public const string Select = "$select";
      public const string Top = "$top";
      public const string Skip = "$skip";
      // Annotations
      public const string SingletonAnnotation = "OData.Singleton";
      public const string DefaultNamespace = "Default";
      // Prefer header
      public const string PreferenceApplied = "Preference-Applied";
      public const string MaxPageSize = "odata.maxpagesize";
  }
  ```
- Extend existing `src/BMMDL.MetaModel/Utilities/SchemaConstants.cs`
  ```csharp
  // Add platform table names
  public const string AuditLogsTable = "platform.audit_logs";
  public const string EventOutboxTable = "platform.event_outbox";
  public const string UserPreferencesTable = "platform.user_preferences";
  public const string RefreshTokenTable = "platform.refresh_token";
  // Column names
  public const string TenantIdColumn = "tenant_id";
  public const string IsActiveColumn = "is_active";
  public const string IsDeletedColumn = "is_deleted";
  public const string CreatedAtColumn = "created_at";
  public const string ModifiedAtColumn = "modified_at";
  public const string DiscriminatorColumn = "_discriminator";
  // HasStream columns
  public const string MediaContentColumn = "_media_content";
  public const string MediaContentTypeColumn = "_media_content_type";
  public const string MediaETagColumn = "_media_etag";
  ```

**Also add missing codes to** `src/BMMDL.Compiler/ErrorCodes.cs`:
- MOD002, MOD003, MOD016, ANN001-ANN010, OPT001-OPT030

**Files to modify**: ~20+ files across Runtime, Runtime.Api, Compiler (replace string literals with constants)

**Dependencies**: None (can run in parallel with R1-R4)

---

## R6: Extract SqlTypeMapper

**Problem**: BMMDL-type-to-SQL-type mapping duplicated in 3 files with inconsistent type coverage (e.g., DateTime → TIMESTAMPTZ vs TIMESTAMP).

**Files to create**:
- `src/BMMDL.MetaModel/Utilities/SqlTypeMapper.cs` — single source of truth for type mapping, including `MapToSqlType()`, `ExtractLength()`, `ExtractDecimalParams()`

**Files to modify**:
- `src/BMMDL.Registry/Services/PgSqlActionGenerator.cs` — use SqlTypeMapper
- `src/BMMDL.Registry/Services/SyncTriggerGenerator.cs` — use SqlTypeMapper
- `src/BMMDL.SchemaManager/PostgresSchemaManager.cs` — use SqlTypeMapper

**Dependencies**: None

---

## R7: Create BmExpressionWalker

**Problem**: Expression tree recursive traversal manually reimplemented 4 times with same switch over BmExpression subtypes.

**Files to create**:
- `src/BMMDL.MetaModel/Expressions/BmExpressionWalker.cs`
  ```csharp
  public static class BmExpressionWalker
  {
      public static void Walk(BmExpression expr, Action<BmExpression> visitor) { ... }
      public static bool Any(BmExpression expr, Func<BmExpression, bool> predicate) { ... }
      public static IEnumerable<T> Collect<T>(BmExpression expr, Func<BmExpression, T?> selector) { ... }
  }
  ```

**Files to modify**:
- `src/BMMDL.Compiler/Pipeline/Passes/ModificationPass.cs` — replace `ExpressionReferencesField` and `UpdateExpressionFieldReferences`
- `src/BMMDL.Compiler/Validation/RuleValidator.cs` — replace `ContainsSubqueryOrExists`
- `src/BMMDL.Compiler/Validation/ComputedFieldValidator.cs` — replace `ValidateFieldReferences`
- (Future) any new expression traversal uses BmExpressionWalker

**Dependencies**: None

---

## R8: Consolidate Compiler Element Parsing

**Problem**: Entity element parsing (field/association/composition/action/function/index/constraint iteration) appears 3 times in BmEntityBuilder. `VisitActionDef`/`VisitFunctionDef` duplicated between BmEntityBuilder and BmServiceBuilder. `AddParseWarning` duplicated in 4 builders.

**Files to create**:
- `src/BMMDL.Compiler/Parsing/BuilderBase.cs` — shared base with `_sourceFile`, `_diagnostics`, `_logger`, `AddParseWarning()`
- `src/BMMDL.Compiler/Parsing/ActionFunctionParsingHelper.cs` — shared `ParseAction`/`ParseFunction` methods

**Files to modify**:
- `src/BMMDL.Compiler/Parsing/BmEntityBuilder.cs` — extend BuilderBase, use helper, extract `ProcessStructuralElements()`
- `src/BMMDL.Compiler/Parsing/BmServiceBuilder.cs` — extend BuilderBase, use helper
- `src/BMMDL.Compiler/Parsing/BmEntityElementBuilder.cs` — extend BuilderBase
- `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs` — extend BuilderBase

**Also**:
- Consolidate `BuildForeachStmtFromRule` and `BuildForeachStmtFromAction` (byte-for-byte identical) in BmStatementBuilder
- Replace `TargetKind` strings ("entity", "type", etc.) with enum

**Dependencies**: None

---

## R9: Reintroduce Interfaces for Key Services

**Problem**: `IMetaModelRepository` was deleted. API-layer services have no interfaces. Concrete instantiation blocks testability.

**Files to create**:
- `src/BMMDL.Registry/Repositories/IMetaModelRepository.cs` — reintroduce
- `src/BMMDL.Registry.Api/Services/IModuleCompilationService.cs`
- `src/BMMDL.Registry.Api/Services/ISchemaManagementService.cs`
- `src/BMMDL.Registry.Api/Services/IAdminService.cs`
- `src/BMMDL.Runtime/Services/ICallTargetResolver.cs` (if not already created in R1)

**Files to modify**:
- `src/BMMDL.Registry/Repositories/EfCoreMetaModelRepository.cs` — implement interface
- `src/BMMDL.Registry.Api/Services/AdminService.cs` — implement interface
- `src/BMMDL.Registry.Api/Services/ModuleCompilationService.cs` — implement interface
- `src/BMMDL.Registry.Api/Services/SchemaManagementService.cs` — implement interface
- `src/BMMDL.Registry.Api/Program.cs` — register as interfaces
- `src/BMMDL.Registry.Api/Controllers/AdminController.cs` — inject via interface
- `src/BMMDL.Registry.Api/Controllers/RegistryControllers.cs` — inject via interface
- `src/BMMDL.Runtime.Api/Program.cs` — register via interface where applicable

**Dependencies**: R1 (for ICallTargetResolver)

---

## R10: Controller Responsibility Extraction

**Problem**: Controllers contain business logic that should be in services: junction SQL in EntityReferenceController, view SQL in DynamicViewController, entity existence checks duplicated 4x, permission checking duplicated.

**Files to create**:
- `src/BMMDL.Runtime.Api/Services/IEntityResolver.cs` — service boundary filtering + entity definition lookup
- `src/BMMDL.Runtime.Api/Services/EntityResolver.cs` — implementation
- `src/BMMDL.Runtime.Api/Services/IODataResponseEnricher.cs` — @odata.id, @odata.context, @odata.etag injection

**Extract to existing services**:
- Move junction table SQL from EntityReferenceController → DynamicSqlBuilder (`BuildJunctionInsertQuery`, `BuildJunctionDeleteQuery`)
- Move `VerifyEntityExistsAsync()` → EntityControllerBase (shared by 4 controllers)
- Move `ExtractEntityIdFromODataReference()` → ODataUrlParser utility
- Move Kahn's algorithm from BatchController → `DependencyGraphValidator` utility
- Move cross-join filter rewriting from ODataSystemController → `CrossJoinFilterRewriter`

**Files to modify**:
- `src/BMMDL.Runtime.Api/Controllers/EntityControllerBase.cs` — add `VerifyEntityExistsAsync`
- `src/BMMDL.Runtime.Api/Controllers/EntityReferenceController.cs` — use DynamicSqlBuilder
- `src/BMMDL.Runtime.Api/Controllers/DynamicViewController.cs` — extract ViewQueryBuilder
- `src/BMMDL.Runtime.Api/Controllers/BatchController.cs` — use base class UoW, extract topo sort
- `src/BMMDL.Runtime.Api/Controllers/ODataSystemController.cs` — extract filter rewriter
- `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` — add junction query methods

**Dependencies**: None (but benefits from R9's interface pattern)

---

## Execution Strategy

### Parallel Group A (no dependencies):
- **R1**: StatementExecutor + CallTargetResolver
- **R2**: RowReader + PlatformServiceBase
- **R3**: EntityWriteService consolidation
- **R5**: Constants centralization
- **R6**: SqlTypeMapper
- **R7**: BmExpressionWalker
- **R8**: Compiler element parsing

### Sequential Group B (depends on A):
- **R4**: DeepOperationBase (depends on R1 for EvaluationContext.CreateChild)
- **R9**: Interfaces (depends on R1 for ICallTargetResolver)
- **R10**: Controller extraction (benefits from R9)

### Recommended Team Assignment (10 agents):
| Agent | Phase | Scope |
|-------|-------|-------|
| 1 | R1 | StatementExecutor + CallTargetResolver + ConvertToBool + EvaluationContext.CreateChild |
| 2 | R2 | RowReader + PlatformServiceBase |
| 3 | R3 | EntityWriteService consolidation |
| 4 | R5a | PlatformEntityNames + SchemaConstants extension + apply to Runtime/ |
| 5 | R5b | ODataConstants + apply to Runtime.Api/ |
| 6 | R5c | ErrorCodes extension + apply to Compiler/ |
| 7 | R6 | SqlTypeMapper |
| 8 | R7 | BmExpressionWalker |
| 9 | R8 | BuilderBase + ActionFunctionParsingHelper + TargetKind enum |
| 10 | R10 | EntityResolver + VerifyEntityExistsAsync + junction SQL extraction |

After Group A completes: agents 1-3 pick up R4, R9.

---

## Success Criteria

1. **All existing tests pass** — `dotnet test src/BMMDL.Tests/BMMDL.Tests.Unit.csproj` (2,232 tests)
2. **No new warnings** — `dotnet build BMMDL.sln` clean
3. **No behavioral changes** — pure refactoring, no feature additions
4. **Measurable improvement**:
   - ~2,300 lines of duplication eliminated
   - 5 new interfaces for testability
   - 10+ hard-coded string categories centralized
   - Largest method reduced from 540→~80 lines (FunctionRegistry)

---

## Out of Scope (Deferred)

These were identified in the review but are lower priority or higher risk:

- **BmModel.cs split** (749 lines, ~35 classes) — high risk of merge conflicts
- **RegistryDbContext IEntityTypeConfiguration** — large structural change
- **RegistryControllers.cs split** (7 controllers) — cosmetic, low impact
- **AggregateExpressionResolver sync-over-async** — requires async propagation up the call chain
- **AnnotationMergePass strategy pattern** — complex grammar coupling
- **PostgresDdlGenerator.GenerateTable decomposition** (237 lines) — recently refactored, stabilize first
- **DynamicPlatformUserService split** (620 lines) — needs careful tenant boundary design
- **OutboxStore magic ordinals** — low risk, easy fix but low impact
- **N+1 in RegistryModulesController.GetAll** — performance, not structure
