# BMMDL Plugin Architecture — Detailed Implementation Plan

> **Date**: 2026-03-06
> **Status**: Phase 1-6 DONE (2878 unit + 656 E2E tests), Phase 7 IN PROGRESS (infrastructure extraction)
> **Influences**: Hibernate Envers (MetadataContributor), Webpack Tapable (typed hooks),
> ABP Framework (IDataFilter + [DependsOn]), MediatR (IPipelineBehavior), SAP CAP (annotation-driven)
> **Successors**: `full-stack-plugin-plan.md` (Phase 5-6), `plugin-bootstrap-gap-fix-plan.md` (G1-G5), `plugin-infrastructure-extraction-plan.md` (Phase 7)

---

## Table of Contents

1. [Design Principles](#1-design-principles)
2. [Architecture Overview](#2-architecture-overview)
3. [Core Interfaces](#3-core-interfaces)
4. [Feature Metadata Model](#4-feature-metadata-model)
5. [DDL Integration](#5-ddl-integration)
6. [DML Integration — Typed Hooks](#6-dml-integration--typed-hooks)
7. [API Pipeline — MediatR-Style Behaviors](#7-api-pipeline--mediatr-style-behaviors)
8. [Runtime Filter Toggle — ABP-Style](#8-runtime-filter-toggle--abp-style)
9. [Compiler Integration](#9-compiler-integration)
10. [Feature Registry & Dependency Resolution](#10-feature-registry--dependency-resolution)
11. [DI Registration](#11-di-registration)
12. [Built-in Features to Extract](#12-built-in-features-to-extract)
13. [Implementation Phases](#13-implementation-phases)
14. [File Structure](#14-file-structure)
15. [Migration Strategy](#15-migration-strategy)
16. [Research Sources](#16-research-sources)

---

## 1. Design Principles

```
METADATA-FIRST (from Hibernate Envers):
  Plugins do NOT generate SQL. Plugins enrich metadata on BmEntity.
  Generators read enriched metadata to produce DDL/DML uniformly.

TYPED HOOKS (from Webpack Tapable):
  Each extension point has explicit semantics:
  - Waterfall: each plugin transforms accumulated state, passes to next
  - Bail: first plugin returning non-null wins, rest skipped
  - Tap: all plugins fire, side-effects only

SCOPED FILTERS (from ABP IDataFilter):
  Runtime query filters can be enabled/disabled per-scope via IDisposable.
  Not all-or-nothing — granular control per feature.

EXPLICIT DEPENDENCIES (from ABP Module [DependsOn]):
  Plugins declare dependencies -> topological sort -> guaranteed ordering.
  Circular dependencies detected and rejected at startup.

ANNOTATION-DRIVEN (from SAP CAP):
  Standard behaviors activate automatically from DSL annotations.
  Zero code for standard features (@TenantScoped, @Temporal, etc.)

PIPELINE WRAPPING (from MediatR IPipelineBehavior):
  API-level concerns compose via nested middleware with next() delegation.
  Each behavior can pre-process, post-process, short-circuit, or catch errors.
```

---

## 2. Architecture Overview

```
                        +---------------------------+
                        |   BMMDL Source (.bmmdl)    |
                        |   with @annotations        |
                        +-------------+-------------+
                                      |
                    +-----------------v-----------------+
                    |     COMPILER PIPELINE              |
                    |  (existing 16 passes)              |
                    |         +                          |
                    |  FeatureContributionPass (new)     |
                    |  -> calls IFeatureMetadataContrib  |
                    +-----------------+-----------------+
                                      |
                    +-----------------v-----------------+
                    |     ENRICHED BmModel               |
                    |  entity.Features[] populated       |
                    |  with BmFeatureMetadata per plugin |
                    +-----------------+-----------------+
                                      |
                +---------------------+---------------------+
                |                     |                     |
    +-----------v----------+ +--------v--------+ +---------v---------+
    |   DDL Generator      | | DML Builder     | |   API Pipeline    |
    |   reads Features[]   | | reads Features[]| |   MediatR-style   |
    |   -> CREATE TABLE    | | -> WHERE/SET    | |   behavior chain  |
    |   -> triggers, RLS   | | Waterfall hooks | |   with next()     |
    +----------------------+ +-----------------+ +-------------------+
```

---

## 3. Core Interfaces

### 3.1 IPlatformFeature — Base Contract

```csharp
namespace BMMDL.Runtime.Plugins;

/// <summary>
/// A platform feature that participates across compile, DDL, DML, and API layers.
/// Each feature implements one or more capability interfaces.
///
/// Ordering: DependsOn (topological) -> Stage (numeric tiebreaker within tier).
/// Activation: AppliesTo() checked per-entity — annotation-driven.
/// </summary>
public interface IPlatformFeature
{
    /// Unique identifier. E.g., "TenantIsolation", "Temporal", "SoftDelete"
    string Name { get; }

    /// Features this one must run after. Resolved via topological sort.
    /// Circular dependencies rejected at startup.
    IReadOnlyList<string> DependsOn => [];

    /// Within the same dependency tier, lower stage runs first.
    int Stage => 0;

    /// Does this feature apply to the given entity?
    bool AppliesTo(BmEntity entity);
}
```

### 3.2 Capability Interfaces

A feature implements only the capabilities it needs:

| Interface | Hook Type | Layer | Purpose |
|---|---|---|---|
| `IFeatureMetadataContributor` | — | Compile | Enrich entity metadata (columns, constraints, filters) |
| `IFeatureQueryFilter` | Waterfall | DML | Transform SELECT query context |
| `IFeatureInsertContributor` | Waterfall | DML | Add columns/values to INSERT |
| `IFeatureUpdateContributor` | Waterfall | DML | Add SET clauses to UPDATE |
| `IFeatureDeleteStrategy` | Bail | DML | Override DELETE behavior |
| `IFeatureWriteHook` | Tap | DML | Before/after lifecycle events |
| `IEntityOperationBehavior` | Pipeline | API | Nested request processing |

---

## 4. Feature Metadata Model

### 4.1 BmFeatureMetadata

```csharp
namespace BMMDL.MetaModel.Features;

/// <summary>
/// Metadata contributed by a single feature for a single entity.
/// Populated at compile time by IFeatureMetadataContributor.
/// Consumed at DDL generation and runtime.
/// </summary>
public sealed class BmFeatureMetadata
{
    public required string FeatureName { get; init; }

    // -- DDL contributions --
    public List<FeatureColumn> Columns { get; } = [];
    public List<FeatureConstraint> Constraints { get; } = [];
    public List<FeatureIndex> Indexes { get; } = [];
    public List<string> PostTableStatements { get; } = [];

    // -- DML contributions (declarative, read at runtime) --
    public List<FeatureQueryFilter> QueryFilters { get; } = [];
    public List<FeatureColumnValue> InsertDefaults { get; } = [];
    public List<FeatureColumnValue> UpdateSets { get; } = [];
    public FeatureDeleteStrategyKind? DeleteStrategy { get; set; }

    // -- API contributions --
    public List<string> StrippedInputFields { get; } = [];
    public List<string> ResponseAnnotations { get; } = [];
}
```

### 4.2 Supporting Records

```csharp
public record FeatureColumn(
    string Name,
    string SqlType,
    bool Nullable,
    string? DefaultExpr,
    string? Comment);

public record FeatureConstraint(
    ConstraintKind Kind,   // Check, Unique, Exclude, ForeignKey
    string Definition);

public record FeatureIndex(
    string[] Columns,
    bool Unique,
    string? Where,
    string? Using);

public record FeatureQueryFilter(
    string Column,
    string Operator,
    FilterValueSource ValueSource,
    object? LiteralValue = null);

public record FeatureColumnValue(
    string Column,
    ColumnValueSource Source,
    object? LiteralValue = null,
    string? Expression = null);

public enum FilterValueSource
{
    TenantId, UserId, Locale, AsOf, ValidAt, Literal, SessionSetting
}

public enum ColumnValueSource
{
    TenantId, UserId, UtcNow, NewUuid, Expression, Literal
}

public enum FeatureDeleteStrategyKind
{
    HardDelete, SoftDelete, TemporalClose
}

public enum ConstraintKind
{
    Check, Unique, Exclude, ForeignKey
}
```

### 4.3 BmEntity Extension

```csharp
// Add to BmEntity
public class BmEntity
{
    // ... existing fields ...

    /// Feature metadata contributed by platform features at compile time.
    /// Ordered by feature dependency resolution.
    public OrderedDictionary<string, BmFeatureMetadata> Features { get; } = new();
}
```

---

## 5. DDL Integration

### 5.1 How PostgresDdlGenerator Changes

The generator no longer knows about specific features. It reads `entity.Features[]` uniformly:

```csharp
// PostgresDdlGenerator.GenerateTable() — AFTER refactoring
private string GenerateTable(BmEntity entity)
{
    var sb = new StringBuilder();
    var tableName = GetQualifiedTableName(entity);
    sb.AppendLine($"CREATE TABLE {tableName} (");

    // 1. Standard entity fields (existing logic, unchanged)
    foreach (var field in entity.Fields)
        sb.AppendLine($"  {GenerateColumn(field, entity)},");

    // 2. FEATURE COLUMNS (uniform loop)
    foreach (var (featureName, meta) in entity.Features)
        foreach (var col in meta.Columns)
        {
            var nullable = col.Nullable ? "" : " NOT NULL";
            var def = col.DefaultExpr != null ? $" DEFAULT {col.DefaultExpr}" : "";
            sb.AppendLine($"  {col.Name} {col.SqlType}{nullable}{def},");
        }

    // 3. Standard constraints (existing: PK, FK, CHECK)
    // ... unchanged ...

    // 4. FEATURE CONSTRAINTS (uniform loop)
    foreach (var (_, meta) in entity.Features)
        foreach (var c in meta.Constraints)
            sb.AppendLine($"  {c.Definition},");

    sb.AppendLine(");");

    // 5. FEATURE INDEXES (uniform loop)
    foreach (var (_, meta) in entity.Features)
        foreach (var idx in meta.Indexes)
        {
            var unique = idx.Unique ? "UNIQUE " : "";
            var where = idx.Where != null ? $" WHERE {idx.Where}" : "";
            var using_ = idx.Using != null ? $" USING {idx.Using}" : "";
            sb.AppendLine($"CREATE {unique}INDEX ON {tableName}{using_} " +
                $"({string.Join(", ", idx.Columns)}){where};");
        }

    // 6. FEATURE POST-TABLE STATEMENTS (triggers, RLS, history tables)
    foreach (var (_, meta) in entity.Features)
        foreach (var stmt in meta.PostTableStatements)
            sb.AppendLine(stmt);

    return sb.ToString();
}
```

### 5.2 Backward Compatibility

All existing DDL output MUST be identical. The sub-generators (TemporalDdlGenerator,
LocalizationDdlGenerator, etc.) are refactored INTO feature contributors that produce
the same metadata. Tests verify DDL output matches before/after.

---

## 6. DML Integration — Typed Hooks

### 6.1 Waterfall Hook: IFeatureQueryFilter

```csharp
namespace BMMDL.Runtime.Plugins;

/// <summary>
/// WATERFALL: each plugin transforms the query context.
/// Output of plugin N becomes input of plugin N+1.
/// Used for SELECT query building.
///
/// Inspired by: Webpack SyncWaterfallHook
/// </summary>
public interface IFeatureQueryFilter : IPlatformFeature
{
    QueryFilterContext ApplyFilter(BmEntity entity, QueryFilterContext ctx);
}

public class QueryFilterContext
{
    public List<WhereClause> WhereClauses { get; } = [];
    public List<NpgsqlParameter> Parameters { get; } = [];
    public string? FromOverride { get; set; }       // Temporal UNION ALL
    public List<string> JoinClauses { get; } = [];

    // Runtime context (read-only for plugins)
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string? Locale { get; init; }
    public DateTimeOffset? AsOf { get; init; }
    public DateTime? ValidAt { get; init; }
    public IFeatureFilterState FilterState { get; init; }

    public int NextParamIndex() => Parameters.Count;
}

public record WhereClause(string Expression);
```

### 6.2 Waterfall Hook: IFeatureInsertContributor / IFeatureUpdateContributor

```csharp
/// WATERFALL: each plugin adds columns/values to INSERT
public interface IFeatureInsertContributor : IPlatformFeature
{
    InsertContext ContributeInsert(BmEntity entity, InsertContext ctx);
}

public class InsertContext
{
    public List<string> Columns { get; } = [];
    public List<string> ValuePlaceholders { get; } = [];
    public List<NpgsqlParameter> Parameters { get; } = [];
    public Dictionary<string, object?> Data { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
}

/// WATERFALL: each plugin adds SET clauses to UPDATE
public interface IFeatureUpdateContributor : IPlatformFeature
{
    UpdateContext ContributeUpdate(BmEntity entity, UpdateContext ctx);
}

public class UpdateContext
{
    public List<string> SetClauses { get; } = [];
    public List<NpgsqlParameter> Parameters { get; } = [];
    public Dictionary<string, object?> Data { get; init; }
    public Dictionary<string, object?>? OldData { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
}
```

### 6.3 Bail Hook: IFeatureDeleteStrategy

```csharp
/// BAIL: first plugin returning non-null wins.
/// Used for DELETE behavior selection (SoftDelete vs TemporalClose vs HardDelete).
///
/// Inspired by: Webpack SyncBailHook
public interface IFeatureDeleteStrategy : IPlatformFeature
{
    /// Return a delete operation, or null to defer to the next plugin.
    DeleteOperation? GetDeleteOperation(BmEntity entity, DeleteContext ctx);
}

public record DeleteOperation(string SqlTemplate, List<NpgsqlParameter> Parameters);

public class DeleteContext
{
    public string TableName { get; init; }
    public string PkCondition { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public Dictionary<string, object?> ExistingData { get; init; }
}
```

### 6.4 Tap Hook: IFeatureWriteHook

```csharp
/// TAP: all plugins fire, side-effects only.
/// Used for lifecycle events (before/after create/update/delete).
///
/// Inspired by: Hibernate PostInsertEventListener, SAP CAP before/after
public interface IFeatureWriteHook : IPlatformFeature
{
    Task OnBeforeInsertAsync(BmEntity entity, Dictionary<string, object?> data,
        WriteContext ctx) => Task.CompletedTask;
    Task OnAfterInsertAsync(BmEntity entity, Dictionary<string, object?> result,
        WriteContext ctx) => Task.CompletedTask;
    Task OnBeforeUpdateAsync(BmEntity entity, Dictionary<string, object?> oldData,
        Dictionary<string, object?> newData, WriteContext ctx) => Task.CompletedTask;
    Task OnAfterUpdateAsync(BmEntity entity, Dictionary<string, object?> result,
        WriteContext ctx) => Task.CompletedTask;
    Task OnBeforeDeleteAsync(BmEntity entity, Dictionary<string, object?> data,
        WriteContext ctx) => Task.CompletedTask;
    Task OnAfterDeleteAsync(BmEntity entity, Dictionary<string, object?> data,
        WriteContext ctx) => Task.CompletedTask;
}

public class WriteContext
{
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public IUnitOfWork UnitOfWork { get; init; }
    public IFeatureFilterState FilterState { get; init; }
}
```

### 6.5 DynamicSqlBuilder Integration

```csharp
// DynamicSqlBuilder.BuildWhereClauses() — AFTER refactoring
private QueryFilterContext ApplyFeatureFilters(BmEntity entity, QueryOptions options)
{
    var ctx = new QueryFilterContext
    {
        TenantId = options.TenantId,
        UserId = options.UserId,
        Locale = options.Locale,
        AsOf = options.AsOf,
        ValidAt = options.ValidAt,
        FilterState = _filterState
    };

    // WATERFALL: each feature transforms ctx
    foreach (var filter in _registry.GetFiltersFor(entity))
    {
        if (_filterState.IsEnabled(filter.Name))
            ctx = filter.ApplyFilter(entity, ctx);
    }

    return ctx;
}

// DynamicSqlBuilder.BuildDeleteQuery() — AFTER refactoring
private DeleteOperation BuildDeleteOperation(BmEntity entity, DeleteContext ctx)
{
    // BAIL: first non-null strategy wins
    foreach (var strategy in _registry.GetDeleteStrategiesFor(entity))
    {
        var op = strategy.GetDeleteOperation(entity, ctx);
        if (op != null) return op;
    }

    // Default: hard DELETE
    return new DeleteOperation(
        $"DELETE FROM {ctx.TableName} WHERE {ctx.PkCondition}", []);
}
```

---

## 7. API Pipeline — MediatR-Style Behaviors

### 7.1 Interface

```csharp
namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Wraps entity CRUD operations at the API level.
/// Russian doll pattern — each behavior wraps the next via next() delegation.
///
/// Inspired by: MediatR IPipelineBehavior<TRequest, TResponse>
/// </summary>
public interface IEntityOperationBehavior : IPlatformFeature
{
    Task<EntityOperationResult> HandleAsync(
        EntityOperationContext context,
        EntityOperationDelegate next,
        CancellationToken ct);
}

public delegate Task<EntityOperationResult> EntityOperationDelegate();

public class EntityOperationContext
{
    public BmEntity Entity { get; init; }
    public CrudOperation Operation { get; init; }
    public Dictionary<string, object?> Data { get; set; }
    public Dictionary<string, object?>? OldData { get; set; }
    public HttpContext HttpContext { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string? IfMatchETag { get; init; }
    public string? Locale { get; init; }
}

public class EntityOperationResult
{
    public bool Success { get; init; }
    public Dictionary<string, object?>? Data { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, string> ResponseHeaders { get; } = new();
    public List<string> Warnings { get; } = [];
}
```

### 7.2 Pipeline Execution

```
Outermost                                               Innermost
[ETag] -> [InputValidation] -> [Authorization] -> [Rules] -> [Audit] -> [CoreHandler]
  |              |                    |               |          |            |
  | pre: check   | pre: strip        | pre: check    | pre:     | pre: set   | execute
  | If-Match     | computed fields   | permissions   | before   | created_at | SQL
  |              | validate enums    |               | rules    | updated_at |
  |              |                   |               |          |            |
  | post: set    | post: -           | post: -       | post:    | post: -    | return
  | ETag header  |                   |               | after    |            | result
  |              |                   |               | rules +  |            |
  |              |                   |               | events   |            |
```

### 7.3 Pipeline Builder

```csharp
// In PlatformFeatureRegistry
public EntityOperationDelegate BuildPipeline(
    BmEntity entity,
    EntityOperationContext ctx,
    EntityOperationDelegate coreHandler)
{
    var applicable = _apiBehaviors
        .Where(b => b.AppliesTo(entity))
        .Reverse()  // Innermost first, then wrap outward
        .ToList();

    var pipeline = coreHandler;
    foreach (var behavior in applicable)
    {
        var next = pipeline;  // Capture for closure
        pipeline = () => behavior.HandleAsync(ctx, next, CancellationToken.None);
    }
    return pipeline;
}
```

### 7.4 Controller Integration

```csharp
// DynamicEntityController — AFTER refactoring
[HttpPost("{module}/{entity}")]
public async Task<IActionResult> Create(string module, string entity, [FromBody] JsonElement body)
{
    var entityDef = ResolveEntity(module, entity);
    var data = ParseBody(body);

    var ctx = new EntityOperationContext
    {
        Entity = entityDef,
        Operation = CrudOperation.Create,
        Data = data,
        HttpContext = HttpContext,
        TenantId = HttpContext.GetTenantId(),
        UserId = HttpContext.GetUserId(),
        Locale = GetRequestLocale()
    };

    // Core handler = actual SQL execution
    EntityOperationDelegate coreHandler = async () =>
    {
        var (sql, parms) = _sqlBuilder.BuildInsertQuery(entityDef, ctx.Data, ctx.TenantId);
        var result = await _executor.ExecuteReturningAsync(sql, parms);
        return new EntityOperationResult { Success = true, Data = result, StatusCode = 201 };
    };

    // Build nested pipeline: [ETag] -> [Validation] -> [Auth] -> [Rules] -> [Audit] -> core
    var pipeline = _registry.BuildPipeline(entityDef, ctx, coreHandler);

    // Execute
    var opResult = await pipeline();
    return StatusCode(opResult.StatusCode, opResult.Data);
}
```

---

## 8. Runtime Filter Toggle — ABP-Style

### 8.1 IFeatureFilterState

```csharp
namespace BMMDL.Runtime.Plugins;

/// <summary>
/// Scoped per-request. Controls which feature filters are active.
/// Stack-based: supports nested Disable/Enable scopes.
///
/// Inspired by: ABP Framework IDataFilter<T>
///
/// Usage:
///   using (_filterState.Disable("SoftDelete"))
///   {
///       // SoftDelete filter disabled within this scope
///       // TenantIsolation still active
///   }
/// </summary>
public interface IFeatureFilterState
{
    bool IsEnabled(string featureName);
    IDisposable Disable(string featureName);
    IDisposable Enable(string featureName);
}
```

### 8.2 Implementation

```csharp
public class FeatureFilterState : IFeatureFilterState
{
    private readonly Dictionary<string, Stack<bool>> _overrides = new();
    private readonly HashSet<string> _defaultEnabled;

    public FeatureFilterState(IEnumerable<string> enabledByDefault)
        => _defaultEnabled = new(enabledByDefault);

    public bool IsEnabled(string featureName)
    {
        if (_overrides.TryGetValue(featureName, out var stack) && stack.Count > 0)
            return stack.Peek();
        return _defaultEnabled.Contains(featureName);
    }

    public IDisposable Disable(string featureName)
    {
        if (!_overrides.ContainsKey(featureName))
            _overrides[featureName] = new();
        _overrides[featureName].Push(false);
        return new FilterScope(this, featureName);
    }

    public IDisposable Enable(string featureName)
    {
        if (!_overrides.ContainsKey(featureName))
            _overrides[featureName] = new();
        _overrides[featureName].Push(true);
        return new FilterScope(this, featureName);
    }

    private sealed class FilterScope(FeatureFilterState state, string name) : IDisposable
    {
        public void Dispose() => state._overrides[name].Pop();
    }
}
```

### 8.3 Scoped Registration

```csharp
// Registered as Scoped — one instance per HTTP request
builder.Services.AddScoped<IFeatureFilterState>(sp =>
{
    var registry = sp.GetRequiredService<PlatformFeatureRegistry>();
    return new FeatureFilterState(registry.AllFeatureNames);
});
```

---

## 9. Compiler Integration

### 9.1 FeatureContributionPass

```csharp
namespace BMMDL.Compiler.Pipeline.Passes;

/// <summary>
/// New compiler pass: runs after OptimizationPass (Order 60).
/// Invokes all IFeatureMetadataContributor to populate entity.Features[].
/// </summary>
public class FeatureContributionPass : ICompilerPass
{
    public string Name => "FeatureContribution";
    public string Description => "Platform features contribute metadata to entities";
    public int Order => 61;

    private readonly PlatformFeatureRegistry _registry;

    public FeatureContributionPass(PlatformFeatureRegistry registry)
        => _registry = registry;

    public CompilationResult Execute(CompilationContext context)
    {
        var cache = new MetaModelCache(context.Model);
        var fctx = new FeatureContributionContext(context.Model, cache);

        foreach (var entity in context.Model.Entities)
        {
            // Contributors run in dependency order (guaranteed by registry)
            foreach (var contributor in _registry.MetadataContributors)
            {
                if (contributor.AppliesTo(entity))
                    contributor.ContributeMetadata(entity, fctx);
            }
        }

        if (fctx.HasErrors)
            return CompilationResult.Failed(fctx.Diagnostics);

        return CompilationResult.Success;
    }
}
```

### 9.2 IFeatureMetadataContributor

```csharp
/// <summary>
/// Contributes metadata to entities during compilation.
/// This is the PRIMARY extension point for DDL/DML behavior.
///
/// Pattern from: Hibernate MetadataContributor + Envers AuditMetadataGenerator
/// Plugins enrich the model — generators read enriched model uniformly.
/// </summary>
public interface IFeatureMetadataContributor : IPlatformFeature
{
    void ContributeMetadata(BmEntity entity, FeatureContributionContext ctx);
}

public class FeatureContributionContext
{
    public BmModel Model { get; }
    public MetaModelCache Cache { get; }
    public List<Diagnostic> Diagnostics { get; } = [];
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// Access metadata already contributed by dependencies.
    /// Enables cross-plugin interaction without direct coupling.
    public BmFeatureMetadata? GetFeatureMetadata(BmEntity entity, string featureName)
        => entity.Features.TryGetValue(featureName, out var meta) ? meta : null;

    public void ReportError(string code, string message)
        => Diagnostics.Add(new(DiagnosticSeverity.Error, code, message));

    public void ReportWarning(string code, string message)
        => Diagnostics.Add(new(DiagnosticSeverity.Warning, code, message));
}
```

---

## 10. Feature Registry & Dependency Resolution

### 10.1 PlatformFeatureRegistry

```csharp
namespace BMMDL.Runtime.Plugins;

public sealed class PlatformFeatureRegistry
{
    private readonly List<IPlatformFeature> _features;           // Sorted
    private readonly Dictionary<string, IPlatformFeature> _byName;

    // Pre-cached, typed, sorted lists
    public IReadOnlyList<IFeatureMetadataContributor> MetadataContributors { get; }
    public IReadOnlyList<IFeatureQueryFilter> QueryFilters { get; }
    public IReadOnlyList<IFeatureInsertContributor> InsertContributors { get; }
    public IReadOnlyList<IFeatureUpdateContributor> UpdateContributors { get; }
    public IReadOnlyList<IFeatureDeleteStrategy> DeleteStrategies { get; }
    public IReadOnlyList<IFeatureWriteHook> WriteHooks { get; }
    public IReadOnlyList<IEntityOperationBehavior> ApiBehaviors { get; }

    public IReadOnlyList<string> AllFeatureNames { get; }

    public PlatformFeatureRegistry(IEnumerable<IPlatformFeature> features)
    {
        _features = TopologicalSort(features.ToList());
        _byName = _features.ToDictionary(f => f.Name);
        AllFeatureNames = _features.Select(f => f.Name).ToList();

        MetadataContributors = _features.OfType<IFeatureMetadataContributor>().ToList();
        QueryFilters = _features.OfType<IFeatureQueryFilter>().ToList();
        InsertContributors = _features.OfType<IFeatureInsertContributor>().ToList();
        UpdateContributors = _features.OfType<IFeatureUpdateContributor>().ToList();
        DeleteStrategies = _features.OfType<IFeatureDeleteStrategy>().ToList();
        WriteHooks = _features.OfType<IFeatureWriteHook>().ToList();
        ApiBehaviors = _features.OfType<IEntityOperationBehavior>().ToList();
    }

    // Entity-filtered accessors
    public IEnumerable<IFeatureQueryFilter> GetFiltersFor(BmEntity entity)
        => QueryFilters.Where(f => f.AppliesTo(entity));

    public IEnumerable<IFeatureDeleteStrategy> GetDeleteStrategiesFor(BmEntity entity)
        => DeleteStrategies.Where(f => f.AppliesTo(entity));

    public IEnumerable<IFeatureWriteHook> GetWriteHooksFor(BmEntity entity)
        => WriteHooks.Where(f => f.AppliesTo(entity));

    // MediatR-style pipeline builder
    public EntityOperationDelegate BuildPipeline(
        BmEntity entity, EntityOperationContext ctx, EntityOperationDelegate coreHandler)
    {
        var applicable = ApiBehaviors.Where(b => b.AppliesTo(entity)).Reverse().ToList();
        var pipeline = coreHandler;
        foreach (var behavior in applicable)
        {
            var next = pipeline;
            pipeline = () => behavior.HandleAsync(ctx, next, CancellationToken.None);
        }
        return pipeline;
    }

    // Topological sort with cycle detection (Kahn's algorithm)
    private static List<IPlatformFeature> TopologicalSort(List<IPlatformFeature> features)
    {
        var byName = features.ToDictionary(f => f.Name);
        var inDegree = features.ToDictionary(f => f.Name, _ => 0);
        var adj = features.ToDictionary(f => f.Name, _ => new List<string>());

        foreach (var f in features)
            foreach (var dep in f.DependsOn)
            {
                if (!byName.ContainsKey(dep))
                    throw new InvalidOperationException(
                        $"Feature '{f.Name}' depends on unknown feature '{dep}'");
                adj[dep].Add(f.Name);
                inDegree[f.Name]++;
            }

        var queue = new PriorityQueue<string, int>();
        foreach (var (name, deg) in inDegree)
            if (deg == 0) queue.Enqueue(name, byName[name].Stage);

        var sorted = new List<IPlatformFeature>();
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            sorted.Add(byName[name]);
            foreach (var next in adj[name])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                    queue.Enqueue(next, byName[next].Stage);
            }
        }

        if (sorted.Count != features.Count)
            throw new InvalidOperationException(
                "Circular dependency detected among platform features: " +
                string.Join(", ", inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key)));

        return sorted;
    }
}
```

---

## 11. DI Registration

```csharp
// Extension method for clean registration
public static class PlatformFeatureExtensions
{
    public static IServiceCollection AddPlatformFeatures(
        this IServiceCollection services,
        Action<PlatformFeatureBuilder> configure)
    {
        var builder = new PlatformFeatureBuilder();
        configure(builder);

        var registry = new PlatformFeatureRegistry(builder.Features);
        services.AddSingleton(registry);

        services.AddScoped<IFeatureFilterState>(sp =>
            new FeatureFilterState(registry.AllFeatureNames));

        return services;
    }
}

public class PlatformFeatureBuilder
{
    internal List<IPlatformFeature> Features { get; } = [];

    public PlatformFeatureBuilder Add<T>() where T : IPlatformFeature, new()
    {
        Features.Add(new T());
        return this;
    }

    public PlatformFeatureBuilder Add(IPlatformFeature feature)
    {
        Features.Add(feature);
        return this;
    }
}

// Usage in Program.cs
builder.Services.AddPlatformFeatures(features =>
{
    // Built-in (extracted from current hardcoded logic)
    features.Add<TenantIsolationFeature>();    // DependsOn: []
    features.Add<SoftDeleteFeature>();         // DependsOn: []
    features.Add<AuditFieldFeature>();         // DependsOn: []
    features.Add<SequenceFeature>();           // DependsOn: []
    features.Add<TemporalFeature>();           // DependsOn: ["TenantIsolation"]
    features.Add<LocalizationFeature>();       // DependsOn: ["TenantIsolation"]
    features.Add<FileReferenceFeature>();      // DependsOn: []
    features.Add<HasStreamFeature>();          // DependsOn: []
    features.Add<InheritanceFeature>();        // DependsOn: []
    features.Add<ETagFeature>();               // DependsOn: []

    // Future (no changes to existing code needed)
    // features.Add<ChangeDataCaptureFeature>();
    // features.Add<FieldEncryptionFeature>();
    // features.Add<DataMaskingFeature>();
});
```

---

## 12. Built-in Features to Extract

| # | Feature | Current Location(s) | Interfaces to Implement |
|---|---------|-------------------|------------------------|
| 1 | **TenantIsolation** | PostgresDdlGenerator, DynamicSqlBuilder, DmlBuilder, TenantContextMiddleware | MetadataContributor, QueryFilter, InsertContributor, UpdateContributor |
| 2 | **SoftDelete** | DynamicSqlBuilder.BuildWhereClauses, DmlBuilder.BuildDeleteQuery | QueryFilter, DeleteStrategy |
| 3 | **Temporal** | TemporalDdlGenerator, TemporalQueryBuilder, DmlBuilder | MetadataContributor, QueryFilter, DeleteStrategy, InsertContributor |
| 4 | **AuditFields** | DmlBuilder (created_at/by, updated_at/by auto-set) | InsertContributor, UpdateContributor |
| 5 | **Localization** | LocalizationDdlGenerator, LocalizationQueryBuilder | MetadataContributor, QueryFilter |
| 6 | **FileReference** | FileReferenceDdlGenerator | MetadataContributor |
| 7 | **HasStream** | PostgresDdlGenerator (3 media columns) | MetadataContributor |
| 8 | **Inheritance** | PostgresDdlGenerator (_discriminator, TPT joins), InheritanceQueryBuilder | MetadataContributor, QueryFilter |
| 9 | **Sequence** | SequenceGenerator, TriggerDdlGenerator | MetadataContributor |
| 10 | **ETag** | DynamicEntityController (If-Match handling) | OperationBehavior |

---

## 13. Implementation Phases

### Phase 1: Foundation (Core Interfaces + Registry)
**Files to create:**
- `src/BMMDL.MetaModel/Features/BmFeatureMetadata.cs` — metadata records
- `src/BMMDL.MetaModel/Features/FeatureTypes.cs` — enums + value records
- `src/BMMDL.Runtime/Plugins/IPlatformFeature.cs` — base interface
- `src/BMMDL.Runtime/Plugins/IFeatureMetadataContributor.cs`
- `src/BMMDL.Runtime/Plugins/IFeatureQueryFilter.cs`
- `src/BMMDL.Runtime/Plugins/IFeatureInsertContributor.cs`
- `src/BMMDL.Runtime/Plugins/IFeatureUpdateContributor.cs`
- `src/BMMDL.Runtime/Plugins/IFeatureDeleteStrategy.cs`
- `src/BMMDL.Runtime/Plugins/IFeatureWriteHook.cs`
- `src/BMMDL.Runtime/Plugins/IEntityOperationBehavior.cs`
- `src/BMMDL.Runtime/Plugins/IFeatureFilterState.cs`
- `src/BMMDL.Runtime/Plugins/FeatureFilterState.cs`
- `src/BMMDL.Runtime/Plugins/PlatformFeatureRegistry.cs`
- `src/BMMDL.Runtime/Plugins/PlatformFeatureExtensions.cs` — DI helpers
- `src/BMMDL.Runtime/Plugins/Contexts/` — QueryFilterContext, InsertContext, etc.

**Tests:**
- Registry topological sort + cycle detection
- FeatureFilterState enable/disable/nested scopes
- Pipeline builder (MediatR-style nesting)

### Phase 2: Proof of Concept — TenantIsolation Feature
**Extract TenantIsolation as first plugin:**
- `src/BMMDL.Runtime/Plugins/Features/TenantIsolationFeature.cs`
- Implements: IFeatureMetadataContributor, IFeatureQueryFilter, IFeatureInsertContributor
- Wire into PostgresDdlGenerator (read entity.Features instead of if/else)
- Wire into DynamicSqlBuilder (use waterfall filter)
- Verify all existing tests pass with identical DDL/DML output

### Phase 3: Extract Remaining Features
**One feature at a time, with test verification after each:**
- SoftDeleteFeature
- TemporalFeature (wraps existing TemporalDdlGenerator + TemporalQueryBuilder)
- AuditFieldFeature
- LocalizationFeature (wraps existing LocalizationDdlGenerator + LocalizationQueryBuilder)
- FileReferenceFeature (wraps existing FileReferenceDdlGenerator)
- HasStreamFeature
- InheritanceFeature
- SequenceFeature
- ETagFeature

### Phase 4: Compiler Integration
- Add FeatureContributionPass (Order 61)
- Wire PlatformFeatureRegistry into CompilerPipeline
- Verify compilation output unchanged

### Phase 5: API Pipeline Refactoring
- Refactor DynamicEntityController to use BuildPipeline()
- Extract ETag, Validation, Authorization, Rules, Audit as IEntityOperationBehavior
- Verify all API integration tests pass

### Phase 6: Prove Extensibility — New Feature
- Implement ChangeDataCaptureFeature or FieldEncryptionFeature
- Zero changes to existing code — only new plugin class + DI registration
- This proves the architecture works for third-party extensions

---

## 14. File Structure

```
src/BMMDL.MetaModel/
  Features/
    BmFeatureMetadata.cs          # Core metadata model
    FeatureTypes.cs               # Enums, records (FeatureColumn, etc.)

src/BMMDL.Runtime/
  Plugins/
    IPlatformFeature.cs           # Base interface
    IFeatureMetadataContributor.cs
    IFeatureQueryFilter.cs
    IFeatureInsertContributor.cs
    IFeatureUpdateContributor.cs
    IFeatureDeleteStrategy.cs
    IFeatureWriteHook.cs
    IEntityOperationBehavior.cs
    IFeatureFilterState.cs
    FeatureFilterState.cs
    PlatformFeatureRegistry.cs
    PlatformFeatureExtensions.cs

    Contexts/
      QueryFilterContext.cs
      InsertContext.cs
      UpdateContext.cs
      DeleteContext.cs
      WriteContext.cs
      EntityOperationContext.cs
      EntityOperationResult.cs
      FeatureContributionContext.cs

    Features/                      # Built-in feature implementations
      TenantIsolationFeature.cs
      SoftDeleteFeature.cs
      TemporalFeature.cs
      AuditFieldFeature.cs
      LocalizationFeature.cs
      FileReferenceFeature.cs
      HasStreamFeature.cs
      InheritanceFeature.cs
      SequenceFeature.cs
      ETagFeature.cs

src/BMMDL.Compiler/
  Pipeline/Passes/
    FeatureContributionPass.cs    # New compiler pass (Order 61)

src/BMMDL.Tests/
  Plugins/
    PlatformFeatureRegistryTests.cs
    FeatureFilterStateTests.cs
    TenantIsolationFeatureTests.cs
    TemporalFeatureTests.cs
    SoftDeleteFeatureTests.cs
    PipelineBuilderTests.cs
    FeatureContributionPassTests.cs
```

---

## 15. Migration Strategy

### Backward Compatibility Guarantee
- All existing DDL output must be byte-identical before and after refactoring
- All existing DML queries must produce identical SQL
- All existing API responses must be unchanged
- All existing tests must pass without modification

### Approach: Strangler Fig Pattern
1. Create new plugin infrastructure alongside existing code
2. For each feature, create the plugin class that produces identical output
3. Add a feature flag to switch between old path and new plugin path
4. Run both paths in tests, assert identical output
5. Once verified, remove old hardcoded code path
6. Remove feature flag

### Verification
- DDL snapshot tests: generate DDL with old code, generate with new code, diff
- DML parameterized query tests: compare SQL + parameter lists
- API integration tests: existing test suite is the regression safety net

---

## 16. Research Sources

### Hibernate ORM
- Integrator SPI: ServiceLoader discovery, EventListenerRegistry, MetadataContributor
- Envers: canonical plugin — audit tables via MetadataContributor, DML via PostInsertEventListener
- @TenantId (Hibernate 6): automatic tenant filtering at SQL generation level
- @Filter/@FilterDef: parameterized WHERE clause injection per Session
- Limitations: no Integrator ordering, native SQL bypasses event system

### Webpack Tapable
- Nine hook types: SyncHook, SyncBailHook, SyncWaterfallHook, SyncLoopHook + async variants
- Waterfall: output of plugin N becomes input of N+1 (used for SQL builder state transformation)
- Bail: first non-null return wins (used for delete strategy selection)
- stage + before options for ordering
- Interception API for cross-cutting monitoring

### EF Core
- IQueryExpressionInterceptor: modify LINQ expression tree before SQL generation
- SaveChangesInterceptor: hook into SaveChanges pipeline for audit, soft-delete
- Global Query Filters: declarative WHERE injection (EF 10 named filters for selective disable)
- IConventionSetPlugin: plugin participation in model building

### ABP Framework
- IDataFilter<T>: runtime enable/disable via scoped IDisposable — key innovation
- [DependsOn] module dependencies with topological ordering
- ISoftDelete + IMultiTenant marker interfaces for opt-in
- Entity Extension System: cross-module entity modification without source changes
- AbpDbContext.ApplyAbpConcepts: unified change tracking for audit, soft-delete, tenant

### SAP CAP
- Three-phase handlers: before (listeners) -> on (interceptor stack with next()) -> after (listeners)
- Annotation-driven: @managed auto-populates audit fields, @restrict for authorization
- Generic providers: zero handler code for standard CRUD behavior
- CDS model -> CSN (JSON AST) -> SQL DDL + CQN -> SQL at runtime

### MediatR
- IPipelineBehavior<TRequest, TResponse>: Russian doll / decorator pattern
- Explicit next() delegation for pre/post processing + short-circuit
- Registration order = execution order (deterministic)
- Constrained generics for different pipelines per request type (CQRS)
