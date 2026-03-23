namespace BMMDL.Runtime.Api.Services;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;

/// <summary>
/// Result for property value operations.
/// </summary>
public class PropertyValueResult
{
    public bool IsSuccess { get; init; }
    public object? Value { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int StatusCode { get; init; } = 200;

    public static PropertyValueResult Success(object? value)
        => new() { IsSuccess = true, Value = value, StatusCode = value != null ? 200 : 204 };

    public static PropertyValueResult Error(string code, string message, int statusCode = 400)
        => new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message, StatusCode = statusCode };
}

/// <summary>
/// Handles OData v4 $value property-level raw value access (GET/PUT/DELETE).
/// </summary>
public class PropertyValueService : IPropertyValueService
{
    private readonly IDynamicSqlBuilder _sqlBuilder;
    private readonly IQueryExecutor _queryExecutor;

    public PropertyValueService(
        IDynamicSqlBuilder sqlBuilder,
        IQueryExecutor queryExecutor)
    {
        _sqlBuilder = sqlBuilder;
        _queryExecutor = queryExecutor;
    }

    /// <summary>
    /// Get raw value of a property.
    /// </summary>
    public async Task<PropertyValueResult> GetPropertyValueAsync(
        BmEntity entityDef, Guid id, string property, Guid? tenantId,
        CancellationToken ct = default)
    {
        var options = tenantId.HasValue
            ? QueryOptions.Default.WithTenant(tenantId.Value)
            : QueryOptions.Default;
        var (sql, parameters) = _sqlBuilder.BuildSelectQuery(entityDef, options, id);

        var record = await _queryExecutor.ExecuteSingleAsync(sql, parameters, ct);
        if (record == null)
            return PropertyValueResult.Error("RECORD_NOT_FOUND", $"Record with id '{id}' not found", 404);

        var snakeProperty = NamingConvention.ToSnakeCase(property);
        if (!record.TryGetValue(snakeProperty, out var value))
            return PropertyValueResult.Error("PROPERTY_NOT_FOUND", $"Property '{property}' not found on entity '{entityDef.Name}'", 404);

        return PropertyValueResult.Success(value);
    }

    /// <summary>
    /// Update raw value of a property.
    /// </summary>
    public async Task<PropertyValueResult> UpdatePropertyValueAsync(
        BmEntity entityDef, Guid id, string property, byte[] content, Guid? tenantId,
        CancellationToken ct = default)
    {
        var snakeProperty = NamingConvention.ToSnakeCase(property);
        var data = new Dictionary<string, object?>
        {
            [snakeProperty] = content
        };

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entityDef, id, data, tenantId);
        var affectedRows = await _queryExecutor.ExecuteNonQueryAsync(sql, parameters, ct);

        if (affectedRows == 0)
            return PropertyValueResult.Error("RECORD_NOT_FOUND", $"Record with id '{id}' not found", 404);

        return new PropertyValueResult { IsSuccess = true, StatusCode = 204 };
    }

    /// <summary>
    /// Delete (set to null) the value of a property.
    /// </summary>
    public async Task<PropertyValueResult> DeletePropertyValueAsync(
        BmEntity entityDef, Guid id, string property, Guid? tenantId,
        CancellationToken ct = default)
    {
        var snakeProperty = NamingConvention.ToSnakeCase(property);
        var data = new Dictionary<string, object?>
        {
            [snakeProperty] = null
        };

        var (sql, parameters) = _sqlBuilder.BuildUpdateQuery(entityDef, id, data, tenantId);
        var affectedRows = await _queryExecutor.ExecuteNonQueryAsync(sql, parameters, ct);

        if (affectedRows == 0)
            return PropertyValueResult.Error("RECORD_NOT_FOUND", $"Record with id '{id}' not found", 404);

        return new PropertyValueResult { IsSuccess = true, StatusCode = 204 };
    }
}
