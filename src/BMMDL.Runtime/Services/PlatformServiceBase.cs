namespace BMMDL.Runtime.Services;

using BMMDL.Runtime.DataAccess;
using Npgsql;

/// <summary>
/// Abstract base class for platform services (User, Tenant) that share
/// common data access patterns: CreateCommand, ExecuteSingleAsync, ExecuteListAsync.
/// </summary>
public abstract class PlatformServiceBase
{
    protected readonly IDynamicSqlBuilder _sqlBuilder;
    protected readonly IMetaModelCache _cache;
    protected readonly ITenantConnectionFactory _connectionFactory;

    protected PlatformServiceBase(
        IDynamicSqlBuilder sqlBuilder,
        IMetaModelCache cache,
        ITenantConnectionFactory connectionFactory)
    {
        _sqlBuilder = sqlBuilder ?? throw new ArgumentNullException(nameof(sqlBuilder));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    protected async Task<Dictionary<string, object?>?> ExecuteSingleAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = CreateCommand(connection, sql, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return null;

        return RowReader.ReadRow(reader);
    }

    protected async Task<List<Dictionary<string, object?>>> ExecuteListAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        await using var connection = await _connectionFactory.GetConnectionAsync(null, ct);
        await using var cmd = CreateCommand(connection, sql, parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(RowReader.ReadRow(reader));
        }

        return results;
    }

    protected static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters)
    {
        var cmd = new NpgsqlCommand(sql, connection);
        foreach (var param in parameters)
        {
            cmd.Parameters.Add(new NpgsqlParameter(param.ParameterName, param.Value ?? DBNull.Value));
        }
        return cmd;
    }
}
