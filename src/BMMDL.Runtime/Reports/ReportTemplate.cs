namespace BMMDL.Runtime.Reports;

using BMMDL.MetaModel.Utilities;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Represents a single column/field to include in a report output.
/// </summary>
public class ReportField
{
    public string Name { get; set; } = "";          // field name from entity
    public string Label { get; set; } = "";         // display label
    public int Width { get; set; } = 150;           // pixel width for PDF column
    public string? Format { get; set; }             // "date" | "datetime" | "currency" | "percent" | null
    public string? Aggregate { get; set; }          // "sum" | "count" | "avg" | "min" | "max" | null
}

/// <summary>
/// Represents a sort criterion applied to a report's result set.
/// </summary>
public class SortConfig
{
    public string Field { get; set; } = "";
    public string Direction { get; set; } = "asc";  // "asc" | "desc"
}

/// <summary>
/// Represents a saved report template that can be rendered, shared, or scheduled.
/// </summary>
public class ReportTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Module { get; set; } = "";          // e.g. "crm"
    public string EntityType { get; set; } = "";      // e.g. "Customer"
    public string LayoutType { get; set; } = "list";  // "list" | "detail" | "summary"
    public List<ReportField> Fields { get; set; } = [];    // JSONB
    public string? GroupBy { get; set; }              // field name to group by
    public List<SortConfig> SortBy { get; set; } = [];    // JSONB
    public string? Header { get; set; }               // header text/template
    public string? Footer { get; set; }               // footer text/template
    public bool IsPublic { get; set; } = false;       // embeddable / shareable
    public string? ShareToken { get; set; }           // UUID string for public access
    public string? ScheduleCron { get; set; }         // cron expression (e.g. "0 8 * * 1")
    public List<string> ScheduleRecipients { get; set; } = [];  // JSONB, email addresses
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Persistence interface for report template management.
/// </summary>
public interface IReportStore
{
    Task<List<ReportTemplate>> GetAllAsync(Guid tenantId);
    Task<List<ReportTemplate>> GetByModuleAsync(string module, Guid tenantId);
    Task<ReportTemplate?> GetByIdAsync(Guid id, Guid tenantId);
    Task<ReportTemplate?> GetByShareTokenAsync(string token);
    Task<ReportTemplate> CreateAsync(ReportTemplate template, Guid tenantId);
    Task<ReportTemplate> UpdateAsync(ReportTemplate template, Guid tenantId);
    Task DeleteAsync(Guid id, Guid tenantId);
}

/// <summary>
/// PostgreSQL-backed store for report templates.
/// Uses raw Npgsql — same pattern as <see cref="BMMDL.Runtime.Events.WebhookStore"/>.
/// </summary>
public class ReportStore : IReportStore
{
    private readonly string _connectionString;
    private readonly ILogger<ReportStore> _logger;

    private const string TableName = SchemaConstants.ReportTemplateTable;

    public ReportStore(string connectionString, ILogger<ReportStore> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<ReportTemplate>> GetAllAsync(Guid tenantId)
    {
        string sql = $"""
            SELECT id, name, description, module, entity_type, layout_type,
                   fields, group_by, sort_by, header, footer,
                   is_public, share_token, schedule_cron, schedule_recipients,
                   created_at, updated_at
            FROM {TableName}
            WHERE tenant_id = @tenant_id
            ORDER BY created_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        var templates = new List<ReportTemplate>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            templates.Add(ReadTemplate(reader));
        }

        return templates;
    }

    public async Task<List<ReportTemplate>> GetByModuleAsync(string module, Guid tenantId)
    {
        string sql = $"""
            SELECT id, name, description, module, entity_type, layout_type,
                   fields, group_by, sort_by, header, footer,
                   is_public, share_token, schedule_cron, schedule_recipients,
                   created_at, updated_at
            FROM {TableName}
            WHERE module = @module AND tenant_id = @tenant_id
            ORDER BY created_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("module", module);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        var templates = new List<ReportTemplate>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            templates.Add(ReadTemplate(reader));
        }

        return templates;
    }

    public async Task<ReportTemplate?> GetByIdAsync(Guid id, Guid tenantId)
    {
        string sql = $"""
            SELECT id, name, description, module, entity_type, layout_type,
                   fields, group_by, sort_by, header, footer,
                   is_public, share_token, schedule_cron, schedule_recipients,
                   created_at, updated_at
            FROM {TableName}
            WHERE id = @id AND tenant_id = @tenant_id
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadTemplate(reader);
        }

        return null;
    }

    public async Task<ReportTemplate?> GetByShareTokenAsync(string token)
    {
        string sql = $"""
            SELECT id, name, description, module, entity_type, layout_type,
                   fields, group_by, sort_by, header, footer,
                   is_public, share_token, schedule_cron, schedule_recipients,
                   created_at, updated_at
            FROM {TableName}
            WHERE share_token = @token
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("token", Guid.Parse(token));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadTemplate(reader);
        }

        return null;
    }

    public async Task<ReportTemplate> CreateAsync(ReportTemplate template, Guid tenantId)
    {
        string sql = $"""
            INSERT INTO {TableName}
                (name, description, module, entity_type, layout_type,
                 fields, group_by, sort_by, header, footer,
                 is_public, share_token, schedule_cron, schedule_recipients, tenant_id)
            VALUES
                (@name, @description, @module, @entity_type, @layout_type,
                 @fields, @group_by, @sort_by, @header, @footer,
                 @is_public, @share_token, @schedule_cron, @schedule_recipients, @tenant_id)
            RETURNING id, name, description, module, entity_type, layout_type,
                      fields, group_by, sort_by, header, footer,
                      is_public, share_token, schedule_cron, schedule_recipients,
                      created_at, updated_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        AddTemplateParameters(cmd, template);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var created = ReadTemplate(reader);
        _logger.LogInformation("Report template created: {TemplateId} ({Name})", created.Id, created.Name);
        return created;
    }

    public async Task<ReportTemplate> UpdateAsync(ReportTemplate template, Guid tenantId)
    {
        string sql = $"""
            UPDATE {TableName}
            SET name                 = @name,
                description          = @description,
                module               = @module,
                entity_type          = @entity_type,
                layout_type          = @layout_type,
                fields               = @fields,
                group_by             = @group_by,
                sort_by              = @sort_by,
                header               = @header,
                footer               = @footer,
                is_public            = @is_public,
                share_token          = @share_token,
                schedule_cron        = @schedule_cron,
                schedule_recipients  = @schedule_recipients,
                updated_at           = NOW()
            WHERE id = @id AND tenant_id = @tenant_id
            RETURNING id, name, description, module, entity_type, layout_type,
                      fields, group_by, sort_by, header, footer,
                      is_public, share_token, schedule_cron, schedule_recipients,
                      created_at, updated_at
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", template.Id);
        AddTemplateParameters(cmd, template);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Report template {template.Id} not found for update.");
        }

        var updated = ReadTemplate(reader);
        _logger.LogInformation("Report template updated: {TemplateId} ({Name})", updated.Id, updated.Name);
        return updated;
    }

    public async Task DeleteAsync(Guid id, Guid tenantId)
    {
        string sql = $"DELETE FROM {TableName} WHERE id = @id AND tenant_id = @tenant_id";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant_id", tenantId);
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Report template deleted: {TemplateId}", id);
    }

    // --- private helpers ---

    /// <summary>
    /// Adds the data parameters shared by INSERT and UPDATE (excludes id).
    /// </summary>
    private static void AddTemplateParameters(NpgsqlCommand cmd, ReportTemplate template)
    {
        cmd.Parameters.AddWithValue("name", template.Name);
        cmd.Parameters.AddWithValue("description", (object?)template.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("module", template.Module);
        cmd.Parameters.AddWithValue("entity_type", template.EntityType);
        cmd.Parameters.AddWithValue("layout_type", template.LayoutType);
        cmd.Parameters.Add(new NpgsqlParameter("fields", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(template.Fields)
        });
        cmd.Parameters.AddWithValue("group_by", (object?)template.GroupBy ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("sort_by", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(template.SortBy)
        });
        cmd.Parameters.AddWithValue("header", (object?)template.Header ?? DBNull.Value);
        cmd.Parameters.AddWithValue("footer", (object?)template.Footer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("is_public", template.IsPublic);
        cmd.Parameters.AddWithValue("share_token",
            template.ShareToken != null ? (object)Guid.Parse(template.ShareToken) : DBNull.Value);
        cmd.Parameters.AddWithValue("schedule_cron", (object?)template.ScheduleCron ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("schedule_recipients", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(template.ScheduleRecipients)
        });
    }

    private static ReportTemplate ReadTemplate(NpgsqlDataReader reader)
    {
        // Column order matches the SELECT list used in all queries:
        // 0  id
        // 1  name
        // 2  description
        // 3  module
        // 4  entity_type
        // 5  layout_type
        // 6  fields
        // 7  group_by
        // 8  sort_by
        // 9  header
        // 10 footer
        // 11 is_public
        // 12 share_token
        // 13 schedule_cron
        // 14 schedule_recipients
        // 15 created_at
        // 16 updated_at

        var fieldsJson = reader.IsDBNull(6) ? "[]" : reader.GetString(6);
        var sortByJson = reader.IsDBNull(8) ? "[]" : reader.GetString(8);
        var recipientsJson = reader.IsDBNull(14) ? "[]" : reader.GetString(14);

        Guid? shareTokenGuid = reader.IsDBNull(12) ? null : reader.GetGuid(12);

        return new ReportTemplate
        {
            Id = reader.GetGuid(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            Module = reader.GetString(3),
            EntityType = reader.GetString(4),
            LayoutType = reader.GetString(5),
            Fields = JsonSerializer.Deserialize<List<ReportField>>(fieldsJson) ?? [],
            GroupBy = reader.IsDBNull(7) ? null : reader.GetString(7),
            SortBy = JsonSerializer.Deserialize<List<SortConfig>>(sortByJson) ?? [],
            Header = reader.IsDBNull(9) ? null : reader.GetString(9),
            Footer = reader.IsDBNull(10) ? null : reader.GetString(10),
            IsPublic = reader.GetBoolean(11),
            ShareToken = shareTokenGuid?.ToString(),
            ScheduleCron = reader.IsDBNull(13) ? null : reader.GetString(13),
            ScheduleRecipients = JsonSerializer.Deserialize<List<string>>(recipientsJson) ?? [],
            CreatedAt = reader.GetDateTime(15),
            UpdatedAt = reader.IsDBNull(16) ? null : reader.GetDateTime(16)
        };
    }
}
