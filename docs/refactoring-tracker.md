# BMMDL Refactoring Tracker

## Phase 1: Critical Fixes
- [x] 1.1 Fix `IMetaModelRepository` — removed dead interface; `PersistenceStats` moved to `EfCoreMetaModelRepository.cs`
- [x] 1.2a Delete `ConstraintDebugTests.cs` — deleted debug artifact
- [x] 1.2b Fix or delete 6 skipped "Outdated" tests — deleted all 6 stale skipped tests
- [x] 1.2c Remove hardcoded Windows path — fixed E2EFixture.cs with cross-platform path resolution
- [x] 1.2d Investigate `$metadata` skip — removed stale skip from PlatformBootstrapTests.cs
- [x] 1.3 Replace `Console.WriteLine` in FunctionRegistry — now uses ILogger<FunctionRegistry>
- [x] 1.4 Fix `@odata.context` URL — all 8 occurrences fixed from `api/v1` to `api/odata`
- [x] 1.5 Standardize error responses — all controllers now use ODataErrorResponse format

## Phase 2: Centralize Constants & Eliminate Duplication
- [x] 2.1 Create `SchemaConstants` static class (`TenantIdColumn`, `PlatformSchema`, `PrimaryKeyColumn`) — created in MetaModel/Utilities
- [x] 2.2 Consolidate `ToSnakeCase` — 5 duplicates → 1 canonical in NamingConvention
- [x] 2.3 Consolidate `QuoteIdentifier` — 3 duplicates → 1 canonical in NamingConvention
- [x] 2.4 Enforce `NamingConvention.GetForeignKeyName()` — added GetFkColumnName/GetFkFieldName/GetFkCamelFieldName; 15+ sites updated
- [x] 2.5 Extract `GetIdValue()` extension method — DictionaryExtensions.GetIdValue() replacing 14 occurrences in 7 files
- [x] 2.6 Register stateless parsers in DI — ExpandExpressionParser as singleton; Filter/Apply are stateful (kept as new)

## Phase 3: Extract Interfaces for Core Services
- [x] 3.1 `IDynamicSqlBuilder` interface — 17 public methods extracted, 20+ injection sites updated
- [x] 3.2 `IMetaModelCache` interface — 13 properties + 20 methods, all consumers updated
- [x] 3.3 `IRuntimeExpressionEvaluator` interface — 2 Evaluate overloads, 9 injection sites updated
- [x] 3.4 Service interfaces — IEntityQueryService, IEntityWriteService, IEntityValidationService, IMediaStreamService, IPropertyValueService created; DI forwarding registered
- [x] 3.5 Fix `DynamicViewController` — now uses IPermissionChecker, IFieldRestrictionApplier, IMetaModelCache, IDynamicSqlBuilder

## Phase 4: Break God Classes + Deep Refactoring (R1-R10, commit 5eb8120)
- [x] 4.1 `DynamicEntityController` (1505→~400 lines): extract singleton logic, UoW wrapper, compute/prefer/inject helpers; extend `EntityControllerBase`
- [x] 4.2 `BmmdlModelBuilder` (2016→~600 lines): extract `BmEntityBuilder`, `BmServiceBuilder`, `BmRuleBuilder`
- [x] 4.3 `SemanticValidationPass` (1235→4-5 validators): `EntityStructureValidator`, `FieldTypeValidator`, `RelationshipValidator`, `EnumValidator`, `ConstraintValidator`
- [x] 4.4 `DynamicSqlBuilder` (1271→~500 lines): extract `SearchQueryBuilder`, `DmlBuilder`
- [x] 4.5 `Compiler/Program.cs` (985 lines): extract command handlers
- [x] R1: Extract `StatementExecutor` + `CallTargetResolver` — unified statement dispatch from 3 classes with configurable error policy (FailFast/Resilient), `EvaluationContext.CreateChild()`, `ConvertToBool` cleanup (6 new, 10 modified, ~500 lines deduped)
- [x] R2: Extract `RowReader` + `PlatformServiceBase` — shared NpgsqlDataReader utility and abstract base for platform services (2 new, 4 modified, ~130 lines deduped)
- [x] R3: Consolidate `EntityWriteService` — extract `ValidateInputData`, `StoreLocalizedFields`, `EnqueueDomainEvent`, `ValidateETag`, `FetchCurrentRecord` helpers (1 modified, ~44 lines deduped)
- [x] R4: Extract `DeepOperationBase` — shared base for DeepInsert/DeepUpdateHandler with rule execution helpers, split `ProcessNestedCollectionAsync` 225→4 focused methods (1 new, 2 modified, ~250 lines deduped)
- [x] R5a: `PlatformEntityNames` + `SchemaConstants` extension — 5 entity name constants + 6 schema constants, applied to 6 Runtime/ files (1 new, 6 modified)
- [x] R5b: `ODataConstants` — error codes, query options, annotations, prefer header constants, applied to 15 Runtime.Api/ files (1 new, 15 modified)
- [x] R5c: `ErrorCodes` extension — 24 new constants (PASS, LEX, SYN, MOD, ANN, OPT, EXT, INH), applied to 10 Compiler/ files
- [x] R6: Extract `SqlTypeMapper` — unified BMMDL-to-PostgreSQL type mapping from 3 files (1 new, 3 modified, ~150 lines deduped)
- [x] R7: Create `BmExpressionWalker` — generic Walk/Any/Collect for all 20 expression subtypes, replaced manual traversal in 5 files (~210 lines deduped)
- [x] R8: Consolidate compiler parsing — `BuilderBase` + `ActionFunctionParsingHelper`, merged duplicate foreach builders (2 new, 5 modified, ~180 lines deduped)
- [x] R9: Registry service interfaces — `IAdminService`, `IModuleCompilationService`, `ISchemaManagementService`, `ICallTargetResolver` with DI registration (4 new, 6 modified)
- [x] R10: Controller extraction — `EntityResolver` service, `VerifyEntityExistsAsync`, junction SQL to DynamicSqlBuilder, `DependencyGraphValidator`, `CrossJoinFilterRewriter`, `ODataUrlParser` (5 new, 10 modified, ~400 lines deduped)

## Phase 4b: Plugin Architecture (Phase 1-7)
- [x] Plugin Phase 1-4: Core interfaces + 10 built-in features + FeatureContributionPass + annotation validation
- [x] Plugin Phase 5: Full-stack plugins — IPlatformEntityProvider, IAdminPageProvider, IMenuContributor, ISettingsProvider, IBmmdlModuleProvider, IPluginLifecycle, IMigrationProvider, PluginDirectoryLoader, PluginManager, frontend plugin system
- [x] Plugin Phase 6: Legacy fallback cleanup — all cross-cutting behavior through plugin pipeline, bootstrap auto-install, MetaModelCacheManager fallback, System Tenant + default role seeding
- [ ] Plugin Phase 7: Infrastructure extraction — AuditLogging, EventOutbox, Webhooks, Collaboration, Reporting, UserPreferences plugins (IN PROGRESS)
- [ ] Plugin Phase 8: Permission seeding in PlatformIdentityPlugin
- [ ] Plugin Phase 9: Program.cs cleanup — remove standalone EnsureTableAsync blocks

## Phase 5: Fix Architecture Layer Violations
- [ ] 5.1 Remove Compiler → Runtime dependency
- [ ] 5.2 Remove Runtime → Registry direct dependency (PlatformRuntime.cs)
- [ ] 5.3 Remove CodeGen → Registry dependency
- [ ] 5.4 Unify route conventions — migrate `api/v1/{tenantId}/...` to `api/odata/...`
- [ ] 5.5 Unify connection string naming → `IOptions<DatabaseOptions>`

## Phase 6: Frontend Refactoring
- [ ] 6.1 Extract `useEntityDisplay` composable (List + Detail views)
- [ ] 6.2 Extract `useCompositionLoader` composable (Create + Edit views)
- [ ] 6.3 Extract `useEntityNavigation` composable (Create + Edit views)
- [ ] 6.4 Extract `useClientPagination<T>` composable (User + Role management)
- [ ] 6.5 Break `SmartTable.vue` (1288 lines): extract export menu, column chooser, filter popover
- [ ] 6.6 Break `EntityListView.vue` (1033 lines): extract detail panel, URL sync, saved view actions
- [ ] 6.7 Break `SettingsView.vue` (1074 lines): split into tab sub-components
- [ ] 6.8 Fix accessibility: aria-label, role="menu", keyboard nav, window.confirm→useConfirmDialog
- [ ] 6.9 Fix NavigationBinding in computed() → ref + watchEffect in EntityDetailView.vue

## Phase 7: Test Infrastructure
- [ ] 7.1 Add `[Trait("Category", "...")]` to all test classes
- [ ] 7.2 Create shared test data builders (BmEntityBuilder, BmRuleBuilder, BmModelBuilder)
- [ ] 7.3 Consolidate duplicate test helpers (AuthHelper, RuleEngine helpers, fixture verification)
- [ ] 7.4 Consolidate E2E fixtures → single canonical `E2EStep2Fixture`
- [ ] 7.5 Delete deprecated `BMMDL.Tests.E2E/` project
- [ ] 7.6 Wire up Testcontainers for DatabaseIntegrationTestBase tests

## Phase 8: Configuration & DI Cleanup
- [ ] 8.1 Extract DI registration extensions: `AddBmmdlRuntime()`, `AddBmmdlOData()`, `AddBmmdlAuth()`, `AddBmmdlEvents()`
- [ ] 8.2 Move startup `.GetAwaiter().GetResult()` → `IHostedService`
- [ ] 8.3 Replace raw `IConfiguration` injection with `IOptions<T>` in 6 services
- [ ] 8.4 Deduplicate `AdminKeyAuthorizationHandler` across Registry.Api and Runtime.Api
