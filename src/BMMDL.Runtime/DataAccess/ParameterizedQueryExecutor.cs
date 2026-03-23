namespace BMMDL.Runtime.DataAccess;

using System.Linq;
using BMMDL.MetaModel.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

/// <summary>
/// Executes parameterized SQL queries safely using Npgsql.
/// Handles connection management, row mapping, and null value conversion.
/// </summary>
public class ParameterizedQueryExecutor : IQueryExecutor
{
    private readonly ITenantConnectionFactory _connectionFactory;
    private readonly Guid? _tenantId;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly int _commandTimeoutSeconds;
    private readonly ILogger<ParameterizedQueryExecutor> _logger;

    /// <summary>
    /// Default command timeout in seconds.
    /// </summary>
    private const int DefaultCommandTimeoutSeconds = 30;

    /// <summary>
    /// Create a new query executor.
    /// </summary>
    /// <param name="connectionFactory">Connection factory for database access.</param>
    /// <param name="tenantId">Optional default tenant ID for all queries.</param>
    /// <param name="unitOfWork">Optional unit of work for transaction participation.</param>
    /// <param name="commandTimeoutSeconds">Command timeout in seconds (default: 30).</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public ParameterizedQueryExecutor(ITenantConnectionFactory connectionFactory, Guid? tenantId = null, IUnitOfWork? unitOfWork = null, int commandTimeoutSeconds = DefaultCommandTimeoutSeconds, ILogger<ParameterizedQueryExecutor>? logger = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _tenantId = tenantId;
        _unitOfWork = unitOfWork;
        _commandTimeoutSeconds = commandTimeoutSeconds;
        _logger = logger ?? NullLogger<ParameterizedQueryExecutor>.Instance;
    }

    /// <summary>
    /// Execute an action using the UoW connection/transaction if active, or a standalone connection otherwise.
    /// </summary>
    private async Task<T> ExecuteWithConnectionAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction?, Task<T>> action,
        CancellationToken ct)
    {
        if (_unitOfWork is { IsStarted: true })
        {
            return await action(_unitOfWork.Connection, _unitOfWork.Transaction);
        }

        await using var connection = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        return await action(connection, null);
    }

    /// <inheritdoc />
    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithConnectionAsync(async (connection, transaction) =>
        {
            await using var cmd = CreateCommand(connection, sql, parameters, transaction, _commandTimeoutSeconds);

            var result = await ExecuteWithDiagnosticsAsync(cmd,
                static (c, t) => c.ExecuteScalarAsync(t), ct);

            if (result is null || result == DBNull.Value)
                return default;

            var targetType = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(targetType);

            if (underlyingType != null)
            {
                if (result == null) return default;
                return (T?)Convert.ChangeType(result, underlyingType);
            }

            return (T?)Convert.ChangeType(result, targetType);
        }, ct);
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>?> ExecuteSingleAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithConnectionAsync(async (connection, transaction) =>
        {
            await using var cmd = CreateCommand(connection, sql, parameters, transaction, _commandTimeoutSeconds);
            await using var reader = await ExecuteWithDiagnosticsAsync(cmd,
                static (c, t) => c.ExecuteReaderAsync(t), ct);

            if (!await reader.ReadAsync(ct))
                return null;

            return RowReader.ReadRow(reader);
        }, ct);
    }

    /// <inheritdoc />
    public async Task<List<Dictionary<string, object?>>> ExecuteListAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithConnectionAsync(async (connection, transaction) =>
        {
            await using var cmd = CreateCommand(connection, sql, parameters, transaction, _commandTimeoutSeconds);
            await using var reader = await ExecuteWithDiagnosticsAsync(cmd,
                static (c, t) => c.ExecuteReaderAsync(t), ct);

            var results = new List<Dictionary<string, object?>>();

            while (await reader.ReadAsync(ct))
            {
                results.Add(RowReader.ReadRow(reader));
            }

            return results;
        }, ct);
    }

    /// <inheritdoc />
    public async Task<int> ExecuteNonQueryAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default)
    {
        return await ExecuteWithConnectionAsync(async (connection, transaction) =>
        {
            await using var cmd = CreateCommand(connection, sql, parameters, transaction, _commandTimeoutSeconds);

            return await ExecuteWithDiagnosticsAsync(cmd,
                static (c, t) => c.ExecuteNonQueryAsync(t), ct);
        }, ct);
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>?> ExecuteReturningAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default)
    {
        // RETURNING clause makes this work like a query
        return await ExecuteSingleAsync(sql, parameters, ct);
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>?> ExecuteTemporalUpdateAsync(
        List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> statements,
        CancellationToken ct = default)
    {
        if (statements == null || statements.Count == 0)
            throw new ArgumentException("At least one statement is required", nameof(statements));

        if (_unitOfWork is { IsStarted: true })
        {
            // Use UoW transaction — all statements run in the same transaction as the rest of the request
            try
            {
                return await ExecuteTemporalStatementsAsync(_unitOfWork.Connection, _unitOfWork.Transaction, statements, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Temporal update failed (UoW): {Message}", ex.Message);
                throw;
            }
        }

        // Fallback: create own connection + transaction (existing behavior for non-UoW contexts)
        await using var connection = await _connectionFactory.GetConnectionAsync(_tenantId, ct);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        try
        {
            var result = await ExecuteTemporalStatementsAsync(connection, transaction, statements, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogWarning(ex, "Temporal update failed: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Execute temporal statements within a given connection and transaction.
    /// For InlineHistory updates, captures the RETURNING result from the first statement (UPDATE)
    /// and uses it to populate unchanged fields in the second statement (INSERT), preventing data loss.
    /// </summary>
    private async Task<Dictionary<string, object?>?> ExecuteTemporalStatementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> statements,
        CancellationToken ct)
    {
        Dictionary<string, object?>? lastResult = null;
        Dictionary<string, object?>? oldRow = null;

        for (int i = 0; i < statements.Count; i++)
        {
            var (sql, parameters) = statements[i];

            // If we have old row data from statement 0, patch NULL placeholders in the INSERT
            if (i > 0 && oldRow != null && sql.Contains("NULL", StringComparison.OrdinalIgnoreCase))
            {
                (sql, parameters) = PatchInsertNullsWithOldValues(sql, parameters, oldRow);
            }

            await using var cmd = CreateCommand(connection, sql, parameters, transaction);

            if (sql.Contains("RETURNING"))
            {
                // Read the RETURNING result
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    var row = RowReader.ReadRow(reader);
                    if (i == 0)
                    {
                        // First statement: capture old row for populating unchanged fields
                        oldRow = row;
                    }
                    // Always update lastResult so the final RETURNING result is returned
                    lastResult = row;
                }
            }
            else
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        return lastResult;
    }

    /// <summary>
    /// Patch an INSERT SQL statement by replacing literal NULL entries in the VALUES clause
    /// with parameterized values from the old row. This prevents data loss for unchanged fields
    /// during InlineHistory temporal updates.
    /// </summary>
    private (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) PatchInsertNullsWithOldValues(
        string sql,
        IReadOnlyList<NpgsqlParameter> originalParameters,
        Dictionary<string, object?> oldRow)
    {
        // Parse the INSERT statement to find column names and value positions
        // Expected format: INSERT INTO schema.table (col1, col2, ...) VALUES (val1, val2, ...) RETURNING *
        var columnsMatch = System.Text.RegularExpressions.Regex.Match(sql, @"INSERT INTO [^\(]+\(([^)]+)\)\s*VALUES\s*\(([^)]+)\)");
        if (!columnsMatch.Success)
        {
            _logger.LogWarning("Temporal INSERT NULL patching: regex did not match SQL structure. SQL: {SqlPrefix}", sql.Substring(0, Math.Min(sql.Length, 200)));
            return (sql, originalParameters);
        }

        var columnNames = columnsMatch.Groups[1].Value.Split(',').Select(c => c.Trim()).ToList();
        var valueTokens = columnsMatch.Groups[2].Value.Split(',').Select(v => v.Trim()).ToList();

        if (columnNames.Count != valueTokens.Count)
            return (sql, originalParameters);

        var newParams = new List<NpgsqlParameter>(originalParameters);
        var modified = false;

        for (int j = 0; j < valueTokens.Count; j++)
        {
            if (!valueTokens[j].Equals("NULL", StringComparison.OrdinalIgnoreCase))
                continue;

            // Convert snake_case column name to PascalCase key for oldRow lookup
            var colName = columnNames[j];
            var pascalKey = NamingConvention.ToPascalCase(colName);

            if (oldRow.TryGetValue(pascalKey, out var oldValue) && oldValue != null)
            {
                var paramName = $"@p_old{newParams.Count}";
                valueTokens[j] = paramName;
                newParams.Add(new NpgsqlParameter(paramName, oldValue));
                modified = true;
            }
        }

        if (!modified)
            return (sql, originalParameters);

        // Rebuild the SQL with patched values using a MatchEvaluator to avoid
        // capture group reference issues (e.g., "$1" in parameter names)
        var newValuesPart = string.Join(", ", valueTokens);
        var newSql = System.Text.RegularExpressions.Regex.Replace(
            sql,
            @"(VALUES\s*\()([^)]+)(\))",
            m => m.Groups[1].Value + newValuesPart + m.Groups[3].Value);

        return (newSql, newParams);
    }

    /// <summary>
    /// Execute a command and wrap PostgresException with the failing SQL for diagnostics.
    /// </summary>
    private static async Task<T> ExecuteWithDiagnosticsAsync<T>(
        NpgsqlCommand cmd,
        Func<NpgsqlCommand, CancellationToken, Task<T>> action,
        CancellationToken ct)
    {
        try
        {
            return await action(cmd, ct);
        }
        catch (PostgresException ex)
        {
            // Add the failing SQL to the exception data for diagnostics
            ex.Data["FailingSQL"] = cmd.CommandText;
            // Log only parameter names and types (not values) to avoid leaking sensitive data
            var paramInfo = string.Join(", ", cmd.Parameters.Cast<NpgsqlParameter>()
                .Select(p => $"{p.ParameterName}:{p.NpgsqlDbType}"));
            ex.Data["Parameters"] = paramInfo;
            throw;
        }
    }

    /// <summary>
    /// Create a command with parameters, optional transaction, and command timeout.
    /// </summary>
    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters,
        NpgsqlTransaction? transaction = null,
        int commandTimeoutSeconds = DefaultCommandTimeoutSeconds)
    {
        var cmd = new NpgsqlCommand(sql, connection);
        cmd.CommandTimeout = commandTimeoutSeconds;
        if (transaction != null) cmd.Transaction = transaction;

        if (parameters is { Count: > 0 })
        {
            foreach (var param in parameters)
            {
                // Clone parameter to avoid reuse issues
                cmd.Parameters.Add(new NpgsqlParameter(param.ParameterName, param.Value ?? DBNull.Value));
            }
        }

        return cmd;
    }

}
