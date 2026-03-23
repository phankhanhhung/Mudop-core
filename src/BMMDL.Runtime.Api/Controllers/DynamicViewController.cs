namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Authorization;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Text.RegularExpressions;

/// <summary>
/// Dynamic REST controller for querying views.
/// Routes: /api/v1/{tenantId}/{module}/views/{viewName}
/// Requires JWT authentication.
/// </summary>
[ApiController]
[Route("api/v1/{tenantId:guid}/{module}/views")]
[Authorize]
public class DynamicViewController : ControllerBase
{
    private readonly IMetaModelCache _cache;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IFieldRestrictionApplier _fieldRestrictionApplier;
    private readonly ILogger<DynamicViewController> _logger;
    private readonly string _connectionString;

    public DynamicViewController(
        IMetaModelCache cache,
        IDynamicSqlBuilder sqlBuilder,
        IPermissionChecker permissionChecker,
        IFieldRestrictionApplier fieldRestrictionApplier,
        ILogger<DynamicViewController> logger,
        IConfiguration configuration)
    {
        _cache = cache;
        _sqlBuilder = sqlBuilder;
        _permissionChecker = permissionChecker;
        _fieldRestrictionApplier = fieldRestrictionApplier;
        _logger = logger;
        _connectionString = configuration.GetConnectionString("TenantDb")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No connection string configured");
    }

    /// <summary>
    /// List all available views.
    /// </summary>
    [HttpGet]
    public IActionResult ListViews([FromRoute] string module)
    {
        var views = _cache.Views
            .Where(v => v.Namespace?.StartsWith(module, StringComparison.OrdinalIgnoreCase) ?? false)
            .Select(v => new
            {
                name = v.Name,
                qualifiedName = v.QualifiedName,
                hasParameters = v.Parameters.Count > 0,
                parameters = v.Parameters.Select(p => new { p.Name, p.Type }).ToList(),
                isMaterialized = v.HasAnnotation("Materialized"),
                isTemporal = v.HasAnnotation("TemporalView") || v.HasAnnotation("Temporal"),
                isCrossModule = v.HasAnnotation("CrossModule"),
                dependencies = ExtractDependencies(v),
                requiredRoles = v.GetAnnotation("RequireRole")?.Value as string
                    ?? v.GetAnnotation("RequireRoles")?.Value as string
            })
            .ToList();

        return Ok(new { views, count = views.Count });
    }

    /// <summary>
    /// Refresh a materialized view.
    /// Only works for views with @Materialized annotation.
    /// </summary>
    /// <param name="module">Module name.</param>
    /// <param name="viewName">View name.</param>
    /// <param name="concurrent">If true, use CONCURRENTLY (requires unique index).</param>
    [HttpPost("{viewName}/refresh")]
    public async Task<IActionResult> RefreshMaterializedView(
        [FromRoute] string module,
        [FromRoute] string viewName,
        [FromQuery] bool concurrent = true,
        CancellationToken ct = default)
    {
        // Find view
        var view = _cache.GetView(viewName) ?? _cache.GetView($"{module}.{viewName}");
        if (view == null)
        {
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.ViewNotFound, $"View '{viewName}' not found"));
        }

        // Verify it's materialized
        if (!view.HasAnnotation("Materialized"))
        {
            return BadRequest(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.ViewNotMaterialized, $"View '{viewName}' is not a materialized view"));
        }

        // Check admin role for refresh
        var userContext = HttpContext.GetUserContext();
        if (userContext == null || !userContext.HasRole("Admin"))
        {
            _logger.LogWarning("Access denied to refresh materialized view {ViewName}: user lacks Admin role", viewName);
            return StatusCode(403, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccessDenied,
                $"Refreshing materialized view '{viewName}' requires the 'Admin' role."));
        }

        try
        {
            var schemaName = BMMDL.MetaModel.Utilities.NamingConvention.GetSchemaName(view.Namespace ?? module);
            var sqlViewName = BMMDL.MetaModel.Utilities.NamingConvention.GetColumnName(view.Name);
            // Quote identifiers to prevent SQL injection
            var qualifiedViewName = $"{NamingConvention.QuoteIdentifier(schemaName)}.{NamingConvention.QuoteIdentifier(sqlViewName)}";

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var refreshSql = concurrent
                ? $"REFRESH MATERIALIZED VIEW CONCURRENTLY {qualifiedViewName}"
                : $"REFRESH MATERIALIZED VIEW {qualifiedViewName}";

            _logger.LogInformation("Refreshing materialized view {ViewName}: {Sql}", viewName, refreshSql);

            var startTime = DateTime.UtcNow;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = refreshSql;
            cmd.CommandTimeout = 300; // 5 minute timeout for large views
            await cmd.ExecuteNonQueryAsync(ct);
            var duration = DateTime.UtcNow - startTime;

            return Ok(new
            {
                view = qualifiedViewName,
                refreshed = true,
                concurrent = concurrent,
                durationMs = duration.TotalMilliseconds,
                refreshedAt = DateTime.UtcNow
            });
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Failed to refresh materialized view {ViewName}", viewName);
            return StatusCode(500, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.ViewRefreshFailed, "View refresh failed. Check server logs for details."));
        }
    }

    /// <summary>
    /// Query a view with OData-style query options.
    /// </summary>
    /// <param name="module">Module name (e.g., "SCM").</param>
    /// <param name="viewName">View name (e.g., "OrderSummary").</param>
    /// <param name="filter">OData $filter expression.</param>
    /// <param name="select">Comma-separated field names to return.</param>
    /// <param name="orderby">Field name to sort by.</param>
    /// <param name="top">Maximum number of items to return.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="asOf">For temporal views: query data as of this timestamp (ISO 8601).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{viewName}")]
    public async Task<IActionResult> QueryView(
        [FromRoute] string module,
        [FromRoute] string viewName,
        [FromQuery(Name = "$filter")] string? filter,
        [FromQuery(Name = "$select")] string? select,
        [FromQuery(Name = "$orderby")] string? orderby,
        [FromQuery(Name = "$top")] int? top,
        [FromQuery(Name = "$skip")] int? skip,
        [FromQuery(Name = "$count")] bool count = false,
        [FromQuery(Name = "$asOf")] DateTime? asOf = null,
        CancellationToken ct = default)
    {
        // Validate pagination parameters
        if (top.HasValue && (top.Value < 0 || top.Value > QueryConstants.MaxPageSize))
            return BadRequest(ODataErrorResponse.FromException("INVALID_TOP",
                $"$top must be between 0 and {QueryConstants.MaxPageSize}"));
        if (skip.HasValue && skip.Value < 0)
            return BadRequest(ODataErrorResponse.FromException("INVALID_SKIP",
                "$skip must be non-negative"));

        // Find view definition
        var view = _cache.GetView(viewName);
        if (view == null)
        {
            // Try qualified name
            view = _cache.GetView($"{module}.{viewName}");
        }
        if (view == null)
        {
            return NotFound(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.ViewNotFound, $"View '{viewName}' not found in module '{module}'"));
        }

        // Check role-based access via @RequireRole annotation
        var requiredRole = view.GetAnnotation("RequireRole")?.Value as string;
        if (!string.IsNullOrEmpty(requiredRole))
        {
            var userContext = HttpContext.GetUserContext();
            if (userContext == null || !userContext.HasRole(requiredRole))
            {
                _logger.LogWarning("Access denied to view {ViewName}: user lacks required role {Role}",
                    view.QualifiedName, requiredRole);
                return StatusCode(403, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccessDenied,
                    $"Access to view '{viewName}' requires role '{requiredRole}'."));
            }
        }

        // Check multiple roles via @RequireRoles annotation (user must have at least one)
        var requiredRoles = view.GetAnnotation("RequireRoles")?.Value as string;
        if (!string.IsNullOrEmpty(requiredRoles))
        {
            var roleList = requiredRoles.Split(',').Select(r => r.Trim()).ToList();
            var userContext = HttpContext.GetUserContext();
            if (userContext == null || !roleList.Any(role => userContext.HasRole(role)))
            {
                _logger.LogWarning("Access denied to view {ViewName}: user lacks any required role from {Roles}",
                    view.QualifiedName, requiredRoles);
                return StatusCode(403, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.AccessDenied,
                    $"Access to view '{viewName}' requires one of: {requiredRoles}."));
            }
        }

        _logger.LogInformation("Querying view {ViewName} with filter={Filter}, top={Top}", 
            view.QualifiedName, filter ?? "(none)", top);

        try
        {
            // Get tenant context using extension method
            var tenantId = HttpContext.GetTenantId();

            // Build view name following PostgreSQL naming convention (quoted to prevent SQL injection)
            var schemaName = BMMDL.MetaModel.Utilities.NamingConvention.GetSchemaName(view.Namespace ?? module);
            var sqlViewName = BMMDL.MetaModel.Utilities.NamingConvention.GetColumnName(view.Name);
            var qualifiedViewName = $"{NamingConvention.QuoteIdentifier(schemaName)}.{NamingConvention.QuoteIdentifier(sqlViewName)}";

            // Check if view is temporal
            var isTemporal = view.HasAnnotation("TemporalView") || view.HasAnnotation("Temporal");

            // Extract view parameters from query string
            var viewParams = new Dictionary<string, object?>();
            foreach (var param in view.Parameters)
            {
                var paramValue = HttpContext.Request.Query[param.Name].FirstOrDefault();
                if (paramValue != null)
                {
                    viewParams[param.Name] = ConvertParameterValue(paramValue, param.Type);
                }
                else if (param.DefaultValue != null)
                {
                    viewParams[param.Name] = param.DefaultValue;
                }
                else
                {
                    // Required parameter missing
                    return BadRequest(ODataErrorResponse.FromException(ODataConstants.ErrorCodes.MissingParameter, $"Required parameter '{param.Name}' is missing"));
                }
            }

            // Build query with parameter substitution (returns parameterized SQL)
            var (sql, queryParams) = BuildViewQuery(qualifiedViewName, filter, select, orderby, top, skip, isTemporal, asOf, viewParams);

            _logger.LogDebug("Executing view query: {Sql}", sql);

            // Execute query
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Set tenant context if available (parameterized to prevent SQL injection)
            if (tenantId.HasValue)
            {
                await using var setCmd = conn.CreateCommand();
                setCmd.CommandText = "SET LOCAL app.current_tenant_id = @tenantId";
                setCmd.Parameters.AddWithValue("tenantId", tenantId.Value.ToString());
                await setCmd.ExecuteNonQueryAsync(ct);
            }

            var results = new List<Dictionary<string, object?>>();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            // Add parameterized query parameters
            foreach (var param in queryParams)
            {
                cmd.Parameters.Add(param);
            }

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[columnName] = value;
                }
                results.Add(row);
            }

            // Get count if requested (reuse the parameterized filter from the main query)
            int? totalCount = null;
            if (count)
            {
                var (countSql, countParams) = BuildCountQuery(qualifiedViewName, filter, isTemporal, asOf, viewParams);

                await using var countCmd = conn.CreateCommand();
                countCmd.CommandText = countSql;
                foreach (var param in countParams)
                {
                    // Clone parameter since it may have been used in main query
                    countCmd.Parameters.Add(new NpgsqlParameter(param.ParameterName, param.Value));
                }
                totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
            }

            // Add count header if requested
            if (totalCount.HasValue)
            {
                Response.Headers["X-Total-Count"] = totalCount.Value.ToString();
            }

            return Ok(new
            {
                value = results,
                count = results.Count,
                totalCount = totalCount
            });
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "Database error querying view {ViewName}", viewName);
            return StatusCode(500, ODataErrorResponse.FromException(ODataConstants.ErrorCodes.DatabaseError, "Database error. Check server logs for details."));
        }
    }

    /// <summary>
    /// Build SQL query for view with OData-style options.
    /// </summary>
    private (string Sql, List<NpgsqlParameter> Parameters) BuildViewQuery(
        string viewName,
        string? filter,
        string? select,
        string? orderby,
        int? top,
        int? skip,
        bool isTemporal = false,
        DateTime? asOf = null,
        Dictionary<string, object?>? viewParams = null)
    {
        var parameters = new List<NpgsqlParameter>();
        var paramIndex = 0;
        
        var selectColumns = string.IsNullOrEmpty(select)
            ? "*"
            : BuildSafeSelectColumns(select);
        var sql = $"SELECT {selectColumns} FROM {viewName}";

        var whereClauses = new List<string>();

        // Temporal: AS OF query (point-in-time) - PARAMETERIZED
        if (isTemporal && asOf.HasValue)
        {
            var paramName = $"@p{paramIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, asOf.Value.ToUniversalTime()));
            whereClauses.Add($"system_start <= {paramName}::TIMESTAMPTZ AND system_end > {paramName}::TIMESTAMPTZ");
        }
        else if (isTemporal && !asOf.HasValue)
        {
            whereClauses.Add("system_end = 'infinity'::TIMESTAMPTZ");
        }

        // Parameterized view: add WHERE clauses for each parameter - PARAMETERIZED
        if (viewParams != null && viewParams.Count > 0)
        {
            foreach (var param in viewParams)
            {
                var columnName = NamingConvention.QuoteIdentifier(BMMDL.MetaModel.Utilities.NamingConvention.GetColumnName(param.Key));
                var paramName = $"@p{paramIndex++}";

                if (param.Value == null)
                {
                    whereClauses.Add($"{columnName} IS NULL");
                }
                else
                {
                    parameters.Add(new NpgsqlParameter(paramName, param.Value));
                    whereClauses.Add($"{columnName} = {paramName}");
                }
            }
        }

        // User-provided filter (already uses parameterized parsing)
        if (!string.IsNullOrEmpty(filter))
        {
            var filterParser = new FilterExpressionParser();
            var (whereClause, filterParams) = filterParser.Parse(filter);

            // Single-pass parameter renaming to prevent collision
            var renumberedClause = whereClause;
            var paramMap = new Dictionary<string, string>();
            foreach (var fp in filterParams)
            {
                var newParamName = $"@p{paramIndex++}";
                paramMap[fp.ParameterName] = newParamName;
                parameters.Add(new NpgsqlParameter(newParamName, fp.Value));
            }
            if (paramMap.Count > 0)
            {
                var pattern = string.Join("|",
                    paramMap.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape))
                    + @"(?=\W|$)";
                renumberedClause = Regex.Replace(renumberedClause, pattern, m => paramMap[m.Value]);
            }
            whereClauses.Add(renumberedClause);
        }

        // Combine WHERE clauses
        if (whereClauses.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", whereClauses);
        }

        // ORDER BY - column names quoted for safety
        if (!string.IsNullOrEmpty(orderby))
        {
            var orderParts = orderby.Split(' ');
            var field = NamingConvention.QuoteIdentifier(BMMDL.MetaModel.Utilities.NamingConvention.GetColumnName(orderParts[0]));
            var direction = orderParts.Length > 1 && orderParts[1].Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? "DESC" : "ASC";
            sql += $" ORDER BY {field} {direction}";
        }

        // LIMIT/OFFSET - integers are safe but use Append pattern for consistency
        if (top.HasValue)
        {
            sql += $" LIMIT {Math.Max(0, top.Value)}";
        }
        if (skip.HasValue)
        {
            sql += $" OFFSET {Math.Max(0, skip.Value)}";
        }

        return (sql, parameters);
    }

    /// <summary>
    /// Build parameterized COUNT query for view.
    /// </summary>
    private (string Sql, List<NpgsqlParameter> Parameters) BuildCountQuery(
        string viewName,
        string? filter,
        bool isTemporal = false,
        DateTime? asOf = null,
        Dictionary<string, object?>? viewParams = null)
    {
        var parameters = new List<NpgsqlParameter>();
        var paramIndex = 0;

        var sql = $"SELECT COUNT(*) FROM {viewName}";
        var whereClauses = new List<string>();

        // Temporal: AS OF query (point-in-time)
        if (isTemporal && asOf.HasValue)
        {
            var paramName = $"@p{paramIndex++}";
            parameters.Add(new NpgsqlParameter(paramName, asOf.Value.ToUniversalTime()));
            whereClauses.Add($"system_start <= {paramName}::TIMESTAMPTZ AND system_end > {paramName}::TIMESTAMPTZ");
        }
        else if (isTemporal && !asOf.HasValue)
        {
            whereClauses.Add("system_end = 'infinity'::TIMESTAMPTZ");
        }

        // View parameters
        if (viewParams != null && viewParams.Count > 0)
        {
            foreach (var param in viewParams)
            {
                var columnName = NamingConvention.QuoteIdentifier(BMMDL.MetaModel.Utilities.NamingConvention.GetColumnName(param.Key));
                var paramName = $"@p{paramIndex++}";

                if (param.Value == null)
                {
                    whereClauses.Add($"{columnName} IS NULL");
                }
                else
                {
                    parameters.Add(new NpgsqlParameter(paramName, param.Value));
                    whereClauses.Add($"{columnName} = {paramName}");
                }
            }
        }

        // User-provided filter
        if (!string.IsNullOrEmpty(filter))
        {
            var filterParser = new FilterExpressionParser();
            var (whereClause, filterParams) = filterParser.Parse(filter);

            // Single-pass parameter renaming to prevent collision
            var renumberedClause = whereClause;
            var paramMap = new Dictionary<string, string>();
            foreach (var fp in filterParams)
            {
                var newParamName = $"@p{paramIndex++}";
                paramMap[fp.ParameterName] = newParamName;
                parameters.Add(new NpgsqlParameter(newParamName, fp.Value));
            }
            if (paramMap.Count > 0)
            {
                var pattern = string.Join("|",
                    paramMap.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape))
                    + @"(?=\W|$)";
                renumberedClause = Regex.Replace(renumberedClause, pattern, m => paramMap[m.Value]);
            }
            whereClauses.Add(renumberedClause);
        }

        if (whereClauses.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", whereClauses);
        }

        return (sql, parameters);
    }

    /// <summary>
    /// Safely build SELECT column list from user input.
    /// Validates each column name (alphanumeric + underscore only),
    /// converts to snake_case, and quotes with double quotes.
    /// </summary>
    private static string BuildSafeSelectColumns(string select)
    {
        var validColPattern = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*$");
        var cols = select
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => validColPattern.IsMatch(c))
            .Select(c => NamingConvention.QuoteIdentifier(BMMDL.MetaModel.Utilities.NamingConvention.GetColumnName(c)))
            .ToList();
        if (cols.Count == 0)
            return "*";
        return string.Join(", ", cols);
    }

    /// <summary>
    /// Convert a string parameter value to the appropriate type.
    /// </summary>
    private static object? ConvertParameterValue(string value, string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "int" or "integer" or "int32" => int.TryParse(value, out var i) ? i : null,
            "long" or "int64" => long.TryParse(value, out var l) ? l : null,
            "decimal" => decimal.TryParse(value, out var d) ? d : null,
            "float" or "double" => double.TryParse(value, out var db) ? db : null,
            "bool" or "boolean" => bool.TryParse(value, out var b) ? b : null,
            "guid" or "uuid" => Guid.TryParse(value, out var g) ? g : null,
            "date" or "datetime" => DateTime.TryParse(value, out var dt) ? dt : null,
            _ => value // Default to string
        };
    }

    /// <summary>
    /// Quote a PostgreSQL identifier to prevent SQL injection.
    /// Double quotes are escaped by doubling them.


    /// <summary>
    /// Extract entity dependencies from a view.
    /// Uses @DependsOn annotation if present, then AST, then regex fallback.
    /// </summary>
    private static List<string> ExtractDependencies(BmView view)
    {
        // Check for explicit @DependsOn annotation
        var dependsOn = view.GetAnnotation("DependsOn")?.Value as string;
        if (!string.IsNullOrEmpty(dependsOn))
        {
            return dependsOn.Split(',').Select(d => d.Trim()).ToList();
        }

        // Use parsed AST if available (more accurate than regex)
        if (view.ParsedSelect != null)
        {
            return ExtractDependenciesFromAst(view.ParsedSelect);
        }

        // Regex fallback for raw string
        var dependencies = new List<string>();
        var selectStatement = view.SelectStatement ?? "";

        var fromPattern = new System.Text.RegularExpressions.Regex(
            @"\b(?:FROM|JOIN)\s+(\w+(?:\.\w+)?)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in fromPattern.Matches(selectStatement))
        {
            var entityRef = match.Groups[1].Value;
            if (!dependencies.Contains(entityRef, StringComparer.OrdinalIgnoreCase))
            {
                dependencies.Add(entityRef);
            }
        }

        return dependencies;
    }

    /// <summary>
    /// Extract entity dependencies from a parsed SELECT AST.
    /// </summary>
    private static List<string> ExtractDependenciesFromAst(BMMDL.MetaModel.Structure.BmSelectStatement stmt)
    {
        var dependencies = new List<string>();

        // FROM source
        if (stmt.From.EntityReference != null)
        {
            AddIfNotPresent(dependencies, stmt.From.EntityReference);
        }
        if (stmt.From.Subquery != null)
        {
            dependencies.AddRange(ExtractDependenciesFromAst(stmt.From.Subquery));
        }

        // JOINs
        foreach (var join in stmt.Joins)
        {
            if (join.Source.EntityReference != null)
            {
                AddIfNotPresent(dependencies, join.Source.EntityReference);
            }
            if (join.Source.Subquery != null)
            {
                dependencies.AddRange(ExtractDependenciesFromAst(join.Source.Subquery));
            }
        }

        // UNION/INTERSECT/EXCEPT
        foreach (var union in stmt.UnionClauses)
        {
            dependencies.AddRange(ExtractDependenciesFromAst(union.Select));
        }

        return dependencies;
    }

    private static void AddIfNotPresent(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
        }
    }
}

