namespace BMMDL.Runtime.Services;

using BMMDL.MetaModel;
using BMMDL.MetaModel.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

/// <summary>
/// Interface for sequence value operations.
/// </summary>
public interface ISequenceService
{
    /// <summary>
    /// Get the next value for a sequence.
    /// </summary>
    Task<string> GetNextValueAsync(string sequenceName, Guid tenantId, Guid? companyId, CancellationToken ct = default);

    /// <summary>
    /// Get the current value for a sequence without incrementing.
    /// </summary>
    Task<long> GetCurrentValueAsync(string sequenceName, Guid tenantId, Guid? companyId, CancellationToken ct = default);

    /// <summary>
    /// Reset a sequence to its start value.
    /// </summary>
    Task ResetSequenceAsync(string sequenceName, Guid tenantId, Guid? companyId, CancellationToken ct = default);

    /// <summary>
    /// Get sequence definition.
    /// </summary>
    BmSequence? GetSequenceDefinition(string sequenceName);
}

/// <summary>
/// Service for generating sequence values using PostgreSQL bmmdl.get_next_sequence function.
/// </summary>
public class SequenceService : ISequenceService
{
    private readonly IMetaModelCache _cache;
    private readonly string _connectionString;
    private readonly ILogger<SequenceService> _logger;

    public SequenceService(
        IMetaModelCache cache,
        IConfiguration configuration,
        ILogger<SequenceService> logger)
    {
        _cache = cache;
        _connectionString = configuration.GetConnectionString("TenantDb")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No connection string configured");
        _logger = logger;
    }

    public BmSequence? GetSequenceDefinition(string sequenceName)
    {
        return _cache.GetSequence(sequenceName);
    }

    public async Task<string> GetNextValueAsync(
        string sequenceName,
        Guid tenantId,
        Guid? companyId,
        CancellationToken ct = default)
    {
        var sequence = _cache.GetSequence(sequenceName);
        if (sequence == null)
        {
            throw new ArgumentException($"Sequence '{sequenceName}' not found");
        }

        _logger.LogDebug("Getting next value for sequence {SequenceName}, tenant={TenantId}", 
            sequenceName, tenantId);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Set session variables for the function using set_config to allow parameterized values
        await using (var setTenantCmd = conn.CreateCommand())
        {
            setTenantCmd.CommandText = "SELECT set_config('app.tenant_id', @tenantId, true)";
            setTenantCmd.Parameters.AddWithValue("@tenantId", tenantId.ToString());
            await setTenantCmd.ExecuteNonQueryAsync(ct);
        }
        await using (var setCompanyCmd = conn.CreateCommand())
        {
            setCompanyCmd.CommandText = "SELECT set_config('app.company_id', @companyId, true)";
            setCompanyCmd.Parameters.AddWithValue("@companyId", (companyId ?? Guid.Empty).ToString());
            await setCompanyCmd.ExecuteNonQueryAsync(ct);
        }

        // Call the sequence function
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT bmmdl.get_next_sequence(@name, @tenant_id, @company_id)";
        cmd.Parameters.AddWithValue("name", sequenceName);
        cmd.Parameters.AddWithValue(SchemaConstants.TenantIdColumn, tenantId);
        cmd.Parameters.AddWithValue("company_id", (object?)companyId ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        var value = result?.ToString() ?? throw new InvalidOperationException("Sequence returned null");

        _logger.LogDebug("Sequence {SequenceName} returned: {Value}", sequenceName, value);
        return value;
    }

    public async Task<long> GetCurrentValueAsync(
        string sequenceName,
        Guid tenantId,
        Guid? companyId,
        CancellationToken ct = default)
    {
        var sequence = _cache.GetSequence(sequenceName);
        if (sequence == null)
        {
            throw new ArgumentException($"Sequence '{sequenceName}' not found");
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Query the sequences table directly
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT current_value 
            FROM bmmdl.sequences 
            WHERE sequence_name = @name 
              AND tenant_id = @tenant_id 
              AND (company_id = @company_id OR (@company_id IS NULL AND company_id IS NULL))";
        cmd.Parameters.AddWithValue("name", sequenceName);
        cmd.Parameters.AddWithValue(SchemaConstants.TenantIdColumn, tenantId);
        cmd.Parameters.AddWithValue("company_id", (object?)companyId ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
    }

    public async Task ResetSequenceAsync(
        string sequenceName,
        Guid tenantId,
        Guid? companyId,
        CancellationToken ct = default)
    {
        var sequence = _cache.GetSequence(sequenceName);
        if (sequence == null)
        {
            throw new ArgumentException($"Sequence '{sequenceName}' not found");
        }

        _logger.LogInformation("Resetting sequence {SequenceName} for tenant={TenantId}", 
            sequenceName, tenantId);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Update the sequences table, resetting to start value
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE bmmdl.sequences 
            SET current_value = @start_value,
                last_reset = NOW()
            WHERE sequence_name = @name 
              AND tenant_id = @tenant_id 
              AND (company_id = @company_id OR (@company_id IS NULL AND company_id IS NULL))";
        cmd.Parameters.AddWithValue("name", sequenceName);
        cmd.Parameters.AddWithValue(SchemaConstants.TenantIdColumn, tenantId);
        cmd.Parameters.AddWithValue("company_id", (object?)companyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("start_value", sequence.StartValue);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected == 0)
        {
            _logger.LogWarning("No sequence row found to reset for {SequenceName}", sequenceName);
        }
    }
}
