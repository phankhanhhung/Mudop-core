namespace BMMDL.Runtime.DataAccess;

/// <summary>
/// Options for querying entities.
/// Supports OData-style parameters for filtering, sorting, and pagination.
/// </summary>
public record QueryOptions
{
    /// <summary>
    /// Filter expression in OData-style format.
    /// </summary>
    /// <example>
    /// "status eq 'Draft'"
    /// "amount gt 1000 and status eq 'Active'"
    /// "name contains 'test'"
    /// </example>
    public string? Filter { get; init; }

    /// <summary>
    /// Order by clause in OData-style format.
    /// </summary>
    /// <example>
    /// "createdAt desc"
    /// "name asc, createdAt desc"
    /// </example>
    public string? OrderBy { get; init; }

    /// <summary>
    /// Comma-separated list of fields to select.
    /// If null, all fields are returned.
    /// </summary>
    /// <example>
    /// "id,name,status"
    /// </example>
    public string? Select { get; init; }

    /// <summary>
    /// Maximum number of records to return (LIMIT).
    /// </summary>
    public int? Top { get; init; }

    /// <summary>
    /// Number of records to skip (OFFSET).
    /// </summary>
    public int? Skip { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenant filtering.
    /// If set, queries will be filtered by this tenant.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// Include soft-deleted records in the results.
    /// Default is false.
    /// </summary>
    public bool IncludeDeleted { get; init; } = false;

    /// <summary>
    /// Navigation properties to expand (OData $expand).
    /// Comma-separated list: "customer,items"
    /// Supports nested options: "customer($select=name)"
    /// </summary>
    public string? Expand { get; init; }

    /// <summary>
    /// Parsed expand options for each navigation property.
    /// Populated by controller after parsing $expand string.
    /// </summary>
    public Dictionary<string, ExpandOptions>? ExpandOptions { get; init; }

    /// <summary>
    /// Locale for localized field resolution (e.g., "en", "de", "fr-FR").
    /// When set, localized fields are resolved via LEFT JOIN to the _texts table
    /// with COALESCE fallback to the default-language value in the main table.
    /// Typically derived from the Accept-Language HTTP header.
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Full-text search query (OData $search).
    /// Searches across all text-searchable fields.
    /// </summary>
    /// <example>
    /// "blue"
    /// "blue OR red"
    /// "blue AND NOT green"
    /// "\"exact phrase\""
    /// </example>
    public string? Search { get; init; }

    /// <summary>
    /// When true, $search uses case-sensitive LIKE instead of case-insensitive ILIKE.
    /// Default is false (case-insensitive).
    /// </summary>
    public bool SearchCaseSensitive { get; init; } = false;

    // ============================================================
    // Phase 8: Temporal Query Options
    // ============================================================
    
    /// <summary>
    /// Point-in-time query for transaction time (system time).
    /// When set, returns data as it existed at this specific moment.
    /// Uses system_start and system_end columns.
    /// </summary>
    /// <example>
    /// AsOf = DateTime.Parse("2024-01-15T12:00:00Z")
    /// </example>
    public DateTimeOffset? AsOf { get; init; }
    
    /// <summary>
    /// Point-in-time query for valid time (business time).
    /// When set, returns data valid at this specific date/time.
    /// Requires entity to have @Temporal.ValidTime annotation.
    /// </summary>
    public DateTime? ValidAt { get; init; }
    
    /// <summary>
    /// Include historical versions in the result.
    /// When true, returns all versions instead of just current.
    /// Default is false.
    /// </summary>
    public bool IncludeHistory { get; init; } = false;
    
    /// <summary>
    /// When true, returns only current record (system_end = 'infinity').
    /// This is the default behavior for temporal entities.
    /// Set to false with IncludeHistory = true to get all versions.
    /// </summary>
    public bool CurrentOnly => !IncludeHistory && !AsOf.HasValue;

    /// <summary>
    /// Create a copy with a specific tenant ID.
    /// </summary>
    public QueryOptions WithTenant(Guid tenantId) => this with { TenantId = tenantId };

    /// <summary>
    /// Create a copy with pagination.
    /// </summary>
    public QueryOptions WithPagination(int top, int skip = 0) => this with { Top = top, Skip = skip };
    
    /// <summary>
    /// Create a copy for point-in-time query.
    /// </summary>
    public QueryOptions AsOfTime(DateTimeOffset asOf) => this with { AsOf = asOf };
    
    /// <summary>
    /// Create a copy to include all historical versions.
    /// </summary>
    public QueryOptions WithHistory() => this with { IncludeHistory = true };

    /// <summary>
    /// Default query options (no filters, no pagination).
    /// </summary>
    public static QueryOptions Default => new();

    /// <summary>
    /// Create options for getting a single entity by ID.
    /// </summary>
    public static QueryOptions ForSingleById(Guid? tenantId = null) => new()
    {
        TenantId = tenantId,
        Top = 1
    };
}

/// <summary>
/// Result of a query operation with pagination information.
/// </summary>
/// <typeparam name="T">Type of items in the result.</typeparam>
public record QueryResult<T>
{
    /// <summary>
    /// The items returned by the query.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Total count of items matching the query (before pagination).
    /// Only populated if CountTotal was requested.
    /// </summary>
    public int? TotalCount { get; init; }

    /// <summary>
    /// Whether there are more items beyond the current page.
    /// </summary>
    public bool HasMore => TotalCount.HasValue && Items.Count < TotalCount.Value;

    /// <summary>
    /// Create an empty result.
    /// </summary>
    public static QueryResult<T> Empty => new() { Items = Array.Empty<T>() };
}
