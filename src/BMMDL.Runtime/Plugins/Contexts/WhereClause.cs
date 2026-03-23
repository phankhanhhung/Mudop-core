namespace BMMDL.Runtime.Plugins.Contexts;

/// <summary>
/// A single WHERE clause expression contributed by a feature filter.
/// </summary>
/// <param name="Expression">The SQL expression for the WHERE clause.</param>
/// <param name="Source">Optional name of the feature that contributed this clause (e.g., "SoftDelete", "TenantIsolation").</param>
public record WhereClause(string Expression, string? Source = null);
