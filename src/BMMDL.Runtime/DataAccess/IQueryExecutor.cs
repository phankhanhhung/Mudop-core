namespace BMMDL.Runtime.DataAccess;

using Npgsql;

/// <summary>
/// Interface for executing parameterized SQL queries.
/// Provides safe, injection-proof database access.
/// </summary>
public interface IQueryExecutor
{
    /// <summary>
    /// Execute a query and return a single scalar value.
    /// </summary>
    /// <typeparam name="T">Expected return type.</typeparam>
    /// <param name="sql">SQL query with parameters.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scalar value, or default if no result.</returns>
    Task<T?> ExecuteScalarAsync<T>(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a query and return a single row as a dictionary.
    /// </summary>
    /// <param name="sql">SQL query with parameters.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The row as a dictionary, or null if no result.</returns>
    Task<Dictionary<string, object?>?> ExecuteSingleAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a query and return multiple rows as dictionaries.
    /// </summary>
    /// <param name="sql">SQL query with parameters.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of rows as dictionaries.</returns>
    Task<List<Dictionary<string, object?>>> ExecuteListAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a non-query command (INSERT, UPDATE, DELETE).
    /// </summary>
    /// <param name="sql">SQL command with parameters.</param>
    /// <param name="parameters">Command parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of affected rows.</returns>
    Task<int> ExecuteNonQueryAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute an INSERT/UPDATE with RETURNING clause and return the row.
    /// </summary>
    /// <param name="sql">SQL command with RETURNING clause.</param>
    /// <param name="parameters">Command parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The returned row as a dictionary.</returns>
    Task<Dictionary<string, object?>?> ExecuteReturningAsync(
        string sql,
        IReadOnlyList<NpgsqlParameter>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute multiple SQL statements in a transaction for temporal updates.
    /// Returns the result of the last statement (which should have RETURNING *).
    /// </summary>
    /// <param name="statements">List of SQL statements with parameters to execute in order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the last statement as a dictionary.</returns>
    Task<Dictionary<string, object?>?> ExecuteTemporalUpdateAsync(
        List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> statements,
        CancellationToken ct = default);
}
