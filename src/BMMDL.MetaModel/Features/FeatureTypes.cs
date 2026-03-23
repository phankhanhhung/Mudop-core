namespace BMMDL.MetaModel.Features;

/// <summary>
/// Source of a value used in automatic query filters.
/// </summary>
public enum FilterValueSource
{
    TenantId,
    UserId,
    Locale,
    AsOf,
    ValidAt,
    Literal,
    SessionSetting
}

/// <summary>
/// Source of a value injected into INSERT/UPDATE statements.
/// </summary>
public enum ColumnValueSource
{
    TenantId,
    UserId,
    UtcNow,
    NewUuid,
    Expression,
    Literal
}

/// <summary>
/// Strategy used when deleting records for a feature-managed entity.
/// </summary>
public enum FeatureDeleteStrategyKind
{
    HardDelete,
    SoftDelete,
    TemporalClose
}

/// <summary>
/// Kind of database constraint contributed by a feature.
/// </summary>
public enum ConstraintKind
{
    Check,
    Unique,
    Exclude,
    ForeignKey
}

/// <summary>
/// A database column contributed by a platform feature.
/// </summary>
/// <param name="Name">Column name (snake_case).</param>
/// <param name="SqlType">PostgreSQL type expression (e.g., "uuid", "timestamptz").</param>
/// <param name="Nullable">Whether the column allows NULL values.</param>
/// <param name="DefaultExpr">Optional SQL default expression.</param>
/// <param name="Comment">Optional column comment for documentation.</param>
public record FeatureColumn(
    string Name,
    string SqlType,
    bool Nullable,
    string? DefaultExpr,
    string? Comment);

/// <summary>
/// A database constraint contributed by a platform feature.
/// </summary>
/// <param name="Kind">The type of constraint (Check, Unique, Exclude, ForeignKey).</param>
/// <param name="Definition">Raw SQL constraint definition.</param>
public record FeatureConstraint(
    ConstraintKind Kind,
    string Definition);

/// <summary>
/// A database index contributed by a platform feature.
/// </summary>
/// <param name="Columns">Column names included in the index.</param>
/// <param name="Unique">Whether this is a unique index.</param>
/// <param name="Where">Optional partial index predicate.</param>
/// <param name="Using">Optional index method (e.g., "btree", "gist").</param>
public record FeatureIndex(
    string[] Columns,
    bool Unique,
    string? Where,
    string? Using);

/// <summary>
/// A declarative query filter applied automatically at runtime.
/// </summary>
/// <param name="Column">Column name to filter on.</param>
/// <param name="Operator">SQL operator (e.g., "=", "&lt;&gt;", "IS").</param>
/// <param name="ValueSource">Where the filter value comes from.</param>
/// <param name="LiteralValue">Literal value when <paramref name="ValueSource"/> is <see cref="FilterValueSource.Literal"/>.</param>
public record FeatureQueryFilter(
    string Column,
    string Operator,
    FilterValueSource ValueSource,
    object? LiteralValue = null);

/// <summary>
/// A column value to inject into INSERT or UPDATE statements.
/// </summary>
/// <param name="Column">Target column name.</param>
/// <param name="Source">Where the value comes from.</param>
/// <param name="LiteralValue">Literal value when <paramref name="Source"/> is <see cref="ColumnValueSource.Literal"/>.</param>
/// <param name="Expression">SQL expression when <paramref name="Source"/> is <see cref="ColumnValueSource.Expression"/>.</param>
public record FeatureColumnValue(
    string Column,
    ColumnValueSource Source,
    object? LiteralValue = null,
    string? Expression = null);
