namespace BMMDL.Runtime.Api.Controllers;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime;
using BMMDL.Runtime.Api.Helpers;
using BMMDL.Runtime.Api.Middleware;
using BMMDL.Runtime.Api.Models;
using BMMDL.Runtime.Constants;
using BMMDL.Runtime.DataAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// OData v4 system-level endpoints: $crossjoin, $all, $openapi.
/// These are OData protocol features that operate across entity sets.
/// </summary>
[ApiController]
[Route("api/odata")]
[Tags("OData System")]
public class ODataSystemController : ControllerBase
{
    private readonly MetaModelCacheManager _cacheManager;
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ILogger<ODataSystemController> _logger;

    private Task<MetaModelCache> GetCacheAsync() => _cacheManager.GetCacheAsync();

    public ODataSystemController(
        MetaModelCacheManager cacheManager,
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor,
        ILogger<ODataSystemController> logger)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _sqlBuilder = sqlBuilder ?? throw new ArgumentNullException(nameof(sqlBuilder));
        _queryExecutor = queryExecutor ?? throw new ArgumentNullException(nameof(queryExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// M32: OData v4 $crossjoin — cross-join two or more entity sets.
    /// GET /api/odata/$crossjoin(EntitySet1,EntitySet2,...)
    /// Supports $filter, $select, $top, $skip query options.
    /// </summary>
    [HttpGet("$crossjoin({entitySets})")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ODataErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CrossJoin(
        [FromRoute] string entitySets,
        [FromQuery(Name = "$filter")] string? filter = null,
        [FromQuery(Name = "$select")] string? select = null,
        [FromQuery(Name = "$top")] int? top = null,
        [FromQuery(Name = "$skip")] int? skip = null,
        CancellationToken ct = default)
    {
        // Validate pagination parameters
        if (top.HasValue && (top.Value < 0 || top.Value > QueryConstants.MaxPageSize))
            return BadRequest(ODataErrorResponse.FromException("INVALID_TOP",
                $"$top must be between 0 and {QueryConstants.MaxPageSize}"));
        if (skip.HasValue && skip.Value < 0)
            return BadRequest(ODataErrorResponse.FromException("INVALID_SKIP",
                "$skip must be non-negative"));

        if (string.IsNullOrWhiteSpace(entitySets))
        {
            return BadRequest(ODataErrorResponse.FromException(
                "CROSSJOIN_NO_ENTITY_SETS",
                "$crossjoin requires at least two entity set names, e.g., $crossjoin(Orders,Products)"));
        }

        var setNames = entitySets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (setNames.Length < 2)
        {
            return BadRequest(ODataErrorResponse.FromException(
                "CROSSJOIN_INSUFFICIENT_SETS",
                "$crossjoin requires at least two entity set names."));
        }

        // Resolve entity definitions and validate all exist
        var cache = await GetCacheAsync();
        var entities = new List<(string name, BmEntity def)>();
        foreach (var setName in setNames)
        {
            var entityDef = cache.GetEntity(setName);
            if (entityDef == null)
            {
                return BadRequest(ODataErrorResponse.FromException(
                    "CROSSJOIN_ENTITY_NOT_FOUND",
                    $"Entity set '{setName}' not found.",
                    setName));
            }
            if (entityDef.IsAbstract)
            {
                return BadRequest(ODataErrorResponse.FromException(
                    "CROSSJOIN_ABSTRACT_ENTITY",
                    $"Cannot cross-join abstract entity '{setName}'.",
                    setName));
            }
            entities.Add((setName, entityDef));
        }

        var tenantId = HttpContext.GetTenantId();

        // Build CROSS JOIN SQL
        var parameters = new List<Npgsql.NpgsqlParameter>();
        var tableAliases = new List<string>();

        // Build FROM clause: table1 t0 CROSS JOIN table2 t1 ...
        var fromParts = new List<string>();
        for (int i = 0; i < entities.Count; i++)
        {
            var alias = $"t{i}";
            tableAliases.Add(alias);
            var tableName = _sqlBuilder.GetTableName(entities[i].def);
            fromParts.Add(i == 0
                ? $"{tableName} {alias}"
                : $"CROSS JOIN {tableName} {alias}");
        }

        // Build SELECT clause: prefix each column with entity name
        var selectColumns = new List<string>();
        if (!string.IsNullOrEmpty(select))
        {
            // Parse qualified selects like "Orders/Id,Products/Name"
            foreach (var col in select.Split(',', StringSplitOptions.TrimEntries))
            {
                var parts = col.Split('/');
                if (parts.Length == 2)
                {
                    var entityIndex = entities.FindIndex(e =>
                        e.name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    if (entityIndex >= 0)
                    {
                        var colName = NamingConvention.ToSnakeCase(parts[1]);
                        selectColumns.Add($"{tableAliases[entityIndex]}.{colName} AS \"{parts[0]}/{parts[1]}\"");
                    }
                }
            }
        }

        if (selectColumns.Count == 0)
        {
            // Default: select all columns from all entities, prefixed
            for (int i = 0; i < entities.Count; i++)
            {
                foreach (var field in entities[i].def.Fields)
                {
                    var colName = NamingConvention.ToSnakeCase(field.Name);
                    var propName = NamingConvention.ToPascalCase(colName);
                    selectColumns.Add($"{tableAliases[i]}.{colName} AS \"{entities[i].name}/{propName}\"");
                }
            }
        }

        var sql = $"SELECT {string.Join(", ", selectColumns)} FROM {string.Join(" ", fromParts)}";

        // WHERE clause: tenant isolation + optional filter
        var whereClauses = new List<string>();
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i].def.TenantScoped && tenantId.HasValue)
            {
                var paramName = $"@tenant{i}";
                whereClauses.Add($"{tableAliases[i]}.tenant_id = {paramName}");
                parameters.Add(new Npgsql.NpgsqlParameter(paramName, tenantId.Value));
            }
        }

        if (!string.IsNullOrEmpty(filter))
        {
            // Parse + parameterize filter: "EntitySet/Field op value" → parameterized SQL with alias.column
            var filterResult = CrossJoinFilterRewriter.Rewrite(filter, entities, tableAliases);
            if (filterResult != null)
            {
                whereClauses.Add(filterResult.Value.WhereClause);
                parameters.AddRange(filterResult.Value.Parameters);
            }
        }

        if (whereClauses.Count > 0)
            sql += $" WHERE {string.Join(" AND ", whereClauses)}";

        // Pagination
        var effectiveTop = Math.Min(top ?? QueryConstants.DefaultPageSize, QueryConstants.MaxPageSize);
        sql += $" LIMIT {effectiveTop}";
        if (skip.HasValue && skip.Value > 0)
            sql += $" OFFSET {skip.Value}";

        _logger.LogInformation("Executing $crossjoin: {EntitySets}", entitySets);

        var results = await _queryExecutor.ExecuteListAsync(sql, parameters, ct);

        var baseUrl = $"{Request.Scheme}://{Request.Host}/api/odata";
        return Ok(new Dictionary<string, object>
        {
            [ODataConstants.JsonProperties.Context] = $"{baseUrl}/$metadata#Collection(Edm.ComplexType)",
            ["value"] = results
        });
    }

    /// <summary>
    /// M33: OData v4 $all — enumerate all entity sets.
    /// GET /api/odata/$all
    /// Returns the list of available entity set names from the meta-model.
    /// </summary>
    [HttpGet("$all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllEntitySets()
    {
        _logger.LogInformation("$all entity set enumeration requested");

        var cache = await GetCacheAsync();
        var baseUrl = $"{Request.Scheme}://{Request.Host}/api/odata";
        var entitySets = cache.Entities
            .Where(e => !e.IsAbstract)
            .Select(e => new
            {
                name = e.Name,
                kind = e.HasAnnotation(ODataConstants.Annotations.Singleton) ? "Singleton" : "EntitySet",
                url = $"{baseUrl}/{e.Namespace ?? ODataConstants.Namespaces.Default}/{e.Name}"
            })
            .ToList();

        return Ok(new Dictionary<string, object>
        {
            [ODataConstants.JsonProperties.Context] = $"{baseUrl}/$metadata",
            ["value"] = entitySets
        });
    }

    /// <summary>
    /// M35: OpenAPI/Swagger stub endpoint.
    /// GET /api/odata/$openapi
    /// Returns 501 Not Implemented with a clear message indicating this feature is planned.
    /// </summary>
    [HttpGet("$openapi")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult GetOpenApi()
    {
        _logger.LogInformation("$openapi requested — feature not yet implemented");

        return StatusCode(StatusCodes.Status501NotImplemented, ODataErrorResponse.FromException(
            "OPENAPI_NOT_IMPLEMENTED",
            "OpenAPI/Swagger document generation from CSDL is not yet implemented. " +
            "Use GET /api/odata/$metadata for the OData CSDL schema. " +
            "This feature is planned for a future release."));
    }
}
