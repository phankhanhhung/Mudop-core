namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text;

/// <summary>
/// Generates SQL for bitemporal entities (InlineHistory and SeparateTables strategies).
/// Internal sub-builder created by DynamicSqlBuilder.
/// </summary>
internal class TemporalQueryBuilder
{
    private readonly DynamicSqlBuilder _parent;

    internal TemporalQueryBuilder(DynamicSqlBuilder parent)
    {
        _parent = parent;
    }

    private static string QuoteTableName(string tableName)
    {
        var dotIdx = tableName.LastIndexOf('.');
        if (dotIdx >= 0)
        {
            var schema = tableName[..dotIdx];
            var table = tableName[(dotIdx + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(table)}";
        }
        return NamingConvention.QuoteIdentifier(tableName);
    }

    private static string BuildHistoryTableName(string tableName)
    {
        var dotIdx = tableName.LastIndexOf('.');
        if (dotIdx >= 0)
        {
            var schema = tableName[..dotIdx];
            var table = tableName[(dotIdx + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(table + "_history")}";
        }
        return NamingConvention.QuoteIdentifier(tableName + "_history");
    }

    /// <summary>
    /// Build temporal UPDATE statements for entities with @Temporal annotation.
    /// Returns multiple SQL statements that must be executed in a transaction.
    ///
    /// For InlineHistory strategy:
    ///   1. Close current record: UPDATE SET system_end = now() WHERE id = @id AND system_end = 'infinity'
    ///   2. Insert new version: INSERT INTO table (...) SELECT ... FROM table WHERE id = @id (with updated values)
    ///
    /// For SeparateTables strategy:
    ///   1. Copy to history: INSERT INTO table_history SELECT * FROM table WHERE id = @id
    ///   2. Update main: UPDATE table SET ..., system_start = now() WHERE id = @id
    /// </summary>
    public List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildTemporalUpdateStatements(
        BmEntity entity,
        Guid id,
        Dictionary<string, object?> data,
        Guid? tenantId = null)
    {
        if (!entity.IsTemporal)
        {
            // For non-temporal entities, just return the regular update
            return new List<(string, IReadOnlyList<NpgsqlParameter>)>
            {
                _parent.BuildUpdateQuery(entity, id, data, tenantId)
            };
        }

        var statements = new List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)>();
        var tableName = _parent.GetRawTableName(entity);

        if (entity.TemporalStrategy == TemporalStrategy.InlineHistory)
        {
            // ============================================================
            // InlineHistory Strategy - Two statements in transaction:
            // 1. UPDATE table SET system_end = now() WHERE id = @id AND system_end = 'infinity' RETURNING *
            // 2. INSERT INTO table (...) VALUES (...) RETURNING *
            // ============================================================

            // Statement 1: Close the current version and capture old values
            var closeParams = new List<NpgsqlParameter>();
            var closeSql = new StringBuilder();
            var quotedTable = QuoteTableName(tableName);
            closeSql.Append($"UPDATE {quotedTable} SET {NamingConvention.QuoteIdentifier("system_end")} = now()");

            var closeIdParam = $"@p{closeParams.Count}";
            closeSql.Append($" WHERE {NamingConvention.QuoteIdentifier("id")} = {closeIdParam} AND {NamingConvention.QuoteIdentifier("system_end")} = 'infinity'::TIMESTAMPTZ");
            closeParams.Add(new NpgsqlParameter(closeIdParam, id));

            if (entity.TenantScoped && tenantId.HasValue)
            {
                var tenantParam = $"@p{closeParams.Count}";
                closeSql.Append($" AND {NamingConvention.QuoteIdentifier(SchemaConstants.TenantIdColumn)} = {tenantParam}");
                closeParams.Add(new NpgsqlParameter(tenantParam, tenantId.Value));
            }

            closeSql.Append(" RETURNING *");
            statements.Add((closeSql.ToString(), closeParams));

            // Statement 2: Insert new version with updated values
            var insertParams = new List<NpgsqlParameter>();
            var columns = new List<string> { NamingConvention.QuoteIdentifier("id") };
            var values = new List<string>();

            var insertIdParam = $"@p{insertParams.Count}";
            values.Add(insertIdParam);
            insertParams.Add(new NpgsqlParameter(insertIdParam, id));

            if (entity.TenantScoped && tenantId.HasValue)
            {
                columns.Add(NamingConvention.QuoteIdentifier(SchemaConstants.TenantIdColumn));
                var tenantParam = $"@p{insertParams.Count}";
                values.Add(tenantParam);
                insertParams.Add(new NpgsqlParameter(tenantParam, tenantId.Value));
            }

            // For each field, use updated value if provided, otherwise emit NULL as placeholder.
            foreach (var field in entity.Fields)
            {
                if (field.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                    continue;

                var colName = NamingConvention.GetColumnName(field.Name);
                columns.Add(NamingConvention.QuoteIdentifier(colName));

                var dataKey = data.Keys.FirstOrDefault(k =>
                    k.Equals(field.Name, StringComparison.OrdinalIgnoreCase));

                if (dataKey != null)
                {
                    var paramName = $"@p{insertParams.Count}";
                    values.Add(paramName);
                    insertParams.Add(new NpgsqlParameter(paramName, DynamicSqlBuilder.ConvertValue(data[dataKey]) ?? DBNull.Value));
                }
                else
                {
                    // For fields not in update payload, we need to get from the RETURNING of UPDATE
                    // This is handled by ExecuteTemporalUpdateAsync which passes old values
                    values.Add("NULL"); // Placeholder - see below
                }
            }

            // Add system time columns
            columns.Add(NamingConvention.QuoteIdentifier("system_start"));
            columns.Add(NamingConvention.QuoteIdentifier("system_end"));
            values.Add("now()");
            values.Add("'infinity'::TIMESTAMPTZ");

            var insertSql = new StringBuilder();
            insertSql.Append($"INSERT INTO {quotedTable} ({string.Join(", ", columns)})");
            insertSql.Append($" VALUES ({string.Join(", ", values)})");
            insertSql.Append(" RETURNING *");

            statements.Add((insertSql.ToString(), insertParams));
        }
        else
        {
            // ============================================================
            // SeparateTables Strategy:
            // 1. Copy current record to history table (with system_end = now())
            // 2. Update main table (with system_start = now())
            // ============================================================

            var quotedHistoryTable = BuildHistoryTableName(tableName);
            var quotedMainTable = QuoteTableName(tableName);

            // Statement 1: Copy to history table
            var copyParams = new List<NpgsqlParameter>();
            var copySql = new StringBuilder();

            copySql.Append($"INSERT INTO {quotedHistoryTable} ");
            copySql.Append($"SELECT *, now() as {NamingConvention.QuoteIdentifier("system_end")} FROM {quotedMainTable}");

            var copyIdParam = $"@p{copyParams.Count}";
            copySql.Append($" WHERE {NamingConvention.QuoteIdentifier("id")} = {copyIdParam}");
            copyParams.Add(new NpgsqlParameter(copyIdParam, id));

            if (entity.TenantScoped && tenantId.HasValue)
            {
                var tenantParam = $"@p{copyParams.Count}";
                copySql.Append($" AND {NamingConvention.QuoteIdentifier(SchemaConstants.TenantIdColumn)} = {tenantParam}");
                copyParams.Add(new NpgsqlParameter(tenantParam, tenantId.Value));
            }

            statements.Add((copySql.ToString(), copyParams));

            // Statement 2: Update main table with new values and reset system_start
            var updateParams = new List<NpgsqlParameter>();
            var setClauses = new List<string>();

            foreach (var kvp in data)
            {
                if (kvp.Key.Equals("Id", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (kvp.Key.Equals("TenantId", StringComparison.OrdinalIgnoreCase))
                    continue;

                var columnName = NamingConvention.GetColumnName(kvp.Key);
                var paramName = $"@p{updateParams.Count}";

                setClauses.Add($"{NamingConvention.QuoteIdentifier(columnName)} = {paramName}");
                updateParams.Add(new NpgsqlParameter(paramName, DynamicSqlBuilder.ConvertValue(kvp.Value) ?? DBNull.Value));
            }

            // Reset system_start to now()
            setClauses.Add($"{NamingConvention.QuoteIdentifier("system_start")} = now()");

            // Update updated_at if entity has it
            if (DynamicSqlBuilder.HasField(entity, "UpdatedAt"))
            {
                var paramName = $"@p{updateParams.Count}";
                setClauses.Add($"{NamingConvention.QuoteIdentifier("updated_at")} = {paramName}");
                updateParams.Add(new NpgsqlParameter(paramName, DateTime.UtcNow));
            }

            var updateSql = new StringBuilder();
            updateSql.Append($"UPDATE {quotedMainTable} SET {string.Join(", ", setClauses)}");

            var updateIdParam = $"@p{updateParams.Count}";
            updateSql.Append($" WHERE {NamingConvention.QuoteIdentifier("id")} = {updateIdParam}");
            updateParams.Add(new NpgsqlParameter(updateIdParam, id));

            if (entity.TenantScoped && tenantId.HasValue)
            {
                var tenantParam = $"@p{updateParams.Count}";
                updateSql.Append($" AND {NamingConvention.QuoteIdentifier(SchemaConstants.TenantIdColumn)} = {tenantParam}");
                updateParams.Add(new NpgsqlParameter(tenantParam, tenantId.Value));
            }

            updateSql.Append(" RETURNING *");

            statements.Add((updateSql.ToString(), updateParams));
        }

        return statements;
    }

    /// <summary>
    /// Returns the FROM source for temporal queries.
    /// For SeparateTables + asOf, returns a UNION ALL subquery wrapping main + history tables
    /// so that deleted/updated records in the history table are included.
    /// For all other cases, returns the plain table name unchanged.
    /// </summary>
    internal (string FromSource, bool IsSubquery) GetTemporalFromSource(
        BmEntity entity,
        QueryOptions options,
        string tableName,
        List<NpgsqlParameter> parameters)
    {
        if (!entity.IsTemporal ||
            entity.TemporalStrategy != TemporalStrategy.SeparateTables ||
            !options.AsOf.HasValue)
        {
            return (tableName, false);
        }

        var quotedMainTable = QuoteTableName(tableName);
        var quotedHistoryTable = BuildHistoryTableName(tableName);
        var paramName = $"@p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(paramName, options.AsOf.Value));

        var sysStart = NamingConvention.QuoteIdentifier("system_start");
        var sysEnd = NamingConvention.QuoteIdentifier("system_end");
        var unionQuery = $"(SELECT * FROM {quotedMainTable} WHERE {sysStart} <= {paramName} AND {paramName} < {sysEnd}" +
                         $" UNION ALL" +
                         $" SELECT * FROM {quotedHistoryTable} WHERE {sysStart} <= {paramName} AND {paramName} < {sysEnd})";

        return (unionQuery, true);
    }

    /// <summary>
    /// Add temporal and valid time filters to WHERE conditions.
    /// For SeparateTables + asOf, the system time filter is already in the UNION FROM source
    /// (via GetTemporalFromSource), so it is skipped here.
    /// </summary>
    internal void AddTemporalFilters(
        List<string> whereClauses,
        BmEntity entity,
        QueryOptions options,
        List<NpgsqlParameter> parameters)
    {
        if (!entity.IsTemporal)
            return;

        // System time filter
        if (options.AsOf.HasValue)
        {
            // For SeparateTables, asOf filtering is handled in the UNION FROM source
            // (GetTemporalFromSource), so skip adding it as a WHERE clause here.
            if (entity.TemporalStrategy != TemporalStrategy.SeparateTables)
            {
                // InlineHistory: all versions in same table, filter directly
                var paramName = $"@p{parameters.Count}";
                var sysStart = NamingConvention.QuoteIdentifier("system_start");
                var sysEnd = NamingConvention.QuoteIdentifier("system_end");
                whereClauses.Add($"{sysStart} <= {paramName} AND {paramName} < {sysEnd}");
                parameters.Add(new NpgsqlParameter(paramName, options.AsOf.Value));
            }
        }
        else if (options.CurrentOnly)
        {
            // Default: return only current records
            whereClauses.Add($"{NamingConvention.QuoteIdentifier("system_end")} = 'infinity'::TIMESTAMPTZ");
        }
        // else: IncludeHistory = true, no temporal filter (return all versions)

        // Valid time filter (for bitemporal entities)
        if (options.ValidAt.HasValue && entity.HasValidTime)
        {
            var validFromCol = !string.IsNullOrEmpty(entity.ValidTimeFromColumn)
                ? NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(entity.ValidTimeFromColumn))
                : NamingConvention.QuoteIdentifier("valid_from");
            var validToCol = !string.IsNullOrEmpty(entity.ValidTimeToColumn)
                ? NamingConvention.QuoteIdentifier(NamingConvention.GetColumnName(entity.ValidTimeToColumn))
                : NamingConvention.QuoteIdentifier("valid_to");

            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"{validFromCol} <= {paramName} AND {paramName} < {validToCol}");
            parameters.Add(new NpgsqlParameter(paramName, options.ValidAt.Value));
        }
    }
}
