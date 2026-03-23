namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text;

/// <summary>
/// Generates SQL for table-per-type inheritance and polymorphic queries.
/// Internal sub-builder created by DynamicSqlBuilder.
/// </summary>
internal class InheritanceQueryBuilder
{
    private readonly DynamicSqlBuilder _parent;

    internal InheritanceQueryBuilder(DynamicSqlBuilder parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Build a SELECT query for a child entity that JOINs the parent table to get inherited fields.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildInheritanceSelectQuery(
        BmEntity entity,
        QueryOptions? options = null,
        Guid? id = null)
    {
        if (entity.ParentEntity == null)
            return _parent.BuildSelectQuery(entity, options, id);

        options ??= QueryOptions.Default;
        var parameters = new List<NpgsqlParameter>();
        var sql = new StringBuilder();

        var childTable = _parent.GetTableName(entity);
        var parentTable = _parent.GetTableName(entity.ParentEntity);

        // Build column list from both parent and child
        var allColumns = new List<string>();

        // Parent fields
        foreach (var field in entity.ParentEntity.Fields)
        {
            if (field.IsVirtual) continue;
            allColumns.Add($"p.{NamingConvention.GetColumnName(field.Name)}");
        }

        // Child-specific fields (exclude inherited)
        var parentFieldNames = new HashSet<string>(
            entity.ParentEntity.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var field in entity.Fields)
        {
            if (field.IsVirtual) continue;
            if (parentFieldNames.Contains(field.Name)) continue;
            allColumns.Add($"c.{NamingConvention.GetColumnName(field.Name)}");
        }

        sql.Append($"SELECT {string.Join(", ", allColumns)}");
        sql.Append($" FROM {childTable} c");
        sql.Append($" INNER JOIN {parentTable} p ON c.id = p.id");

        // WHERE clauses
        var whereClauses = new List<string>();
        if (id.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"c.id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, id.Value));
        }
        if (entity.TenantScoped && options.TenantId.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"p.tenant_id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, options.TenantId.Value));
        }

        // Soft-delete filter (parent table has is_deleted if SoftDeletable)
        if (!options.IncludeDeleted && DynamicSqlBuilder.HasField(entity.ParentEntity, "IsDeleted"))
        {
            whereClauses.Add("p.is_deleted = false");
        }
        else if (!options.IncludeDeleted && DynamicSqlBuilder.HasField(entity, "IsDeleted"))
        {
            whereClauses.Add("c.is_deleted = false");
        }

        _parent.AddCustomFilter(whereClauses, options.Filter, parameters, entity);

        if (whereClauses.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", whereClauses));
        }

        DynamicSqlBuilder.AppendOrderByClause(sql, options.OrderBy, entity);
        DynamicSqlBuilder.AppendPaginationClauses(sql, options.Top, options.Skip, parameters);

        return (sql.ToString(), (IReadOnlyList<NpgsqlParameter>)parameters);
    }

    /// <summary>
    /// Build a polymorphic SELECT for a parent entity that LEFT JOINs all child tables.
    /// </summary>
    public (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildPolymorphicSelectQuery(
        BmEntity parentEntity,
        List<BmEntity> derivedEntities,
        QueryOptions? options = null,
        Guid? id = null)
    {
        if (derivedEntities.Count == 0)
            return _parent.BuildSelectQuery(parentEntity, options, id);

        options ??= QueryOptions.Default;
        var parameters = new List<NpgsqlParameter>();
        var sql = new StringBuilder();

        var parentTable = _parent.GetTableName(parentEntity);

        // Start with parent columns
        var columns = new List<string> { $"p._discriminator" };
        foreach (var field in parentEntity.Fields)
        {
            if (field.IsVirtual) continue;
            columns.Add($"p.{NamingConvention.GetColumnName(field.Name)}");
        }

        // Add child-specific columns (nullable since LEFT JOIN)
        var parentFieldNames = new HashSet<string>(
            parentEntity.Fields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < derivedEntities.Count; i++)
        {
            var alias = $"d{i}";
            foreach (var field in derivedEntities[i].Fields)
            {
                if (field.IsVirtual) continue;
                if (parentFieldNames.Contains(field.Name)) continue;
                columns.Add($"{alias}.{NamingConvention.GetColumnName(field.Name)}");
            }
        }

        sql.Append($"SELECT {string.Join(", ", columns)}");
        sql.Append($" FROM {parentTable} p");

        // LEFT JOIN each derived table
        for (int i = 0; i < derivedEntities.Count; i++)
        {
            var childTable = _parent.GetTableName(derivedEntities[i]);
            sql.Append($" LEFT JOIN {childTable} d{i} ON p.id = d{i}.id");
        }

        // WHERE
        var whereClauses = new List<string>();
        if (id.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"p.id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, id.Value));
        }
        if (parentEntity.TenantScoped && options.TenantId.HasValue)
        {
            var paramName = $"@p{parameters.Count}";
            whereClauses.Add($"p.tenant_id = {paramName}");
            parameters.Add(new NpgsqlParameter(paramName, options.TenantId.Value));
        }

        // Soft-delete filter on parent table
        if (!options.IncludeDeleted && DynamicSqlBuilder.HasField(parentEntity, "IsDeleted"))
        {
            whereClauses.Add("p.is_deleted = false");
        }

        _parent.AddCustomFilter(whereClauses, options.Filter, parameters, parentEntity);

        if (whereClauses.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", whereClauses));
        }

        DynamicSqlBuilder.AppendOrderByClause(sql, options.OrderBy, parentEntity);
        DynamicSqlBuilder.AppendPaginationClauses(sql, options.Top, options.Skip, parameters);

        return (sql.ToString(), (IReadOnlyList<NpgsqlParameter>)parameters);
    }

    /// <summary>
    /// Build INSERT queries for a child entity in a table-per-type hierarchy.
    /// Returns two SQL statements: one for the parent table, one for the child table.
    /// </summary>
    public List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceInsertQueries(
        BmEntity entity,
        Dictionary<string, object?> data,
        Guid? tenantId = null)
    {
        if (entity.ParentEntity == null)
            return new() { _parent.BuildInsertQuery(entity, data, tenantId) };

        var results = new List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)>();

        // Work with a copy to avoid mutating the caller's dictionary
        var workingData = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);

        // Ensure ID is set
        if (!workingData.ContainsKey("Id") && !workingData.ContainsKey("id"))
        {
            workingData["Id"] = Guid.NewGuid();
        }

        // Split data into parent fields and child fields
        var parentFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectAllFieldNames(entity.ParentEntity, parentFieldNames);

        var parentData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var childData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in workingData)
        {
            if (kvp.Key.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                // ID goes to both tables
                parentData[kvp.Key] = kvp.Value;
                childData[kvp.Key] = kvp.Value;
            }
            else if (parentFieldNames.Contains(kvp.Key))
            {
                parentData[kvp.Key] = kvp.Value;
            }
            else
            {
                childData[kvp.Key] = kvp.Value;
            }
        }

        // Add discriminator to parent insert
        parentData[SchemaConstants.DiscriminatorColumn] = entity.DiscriminatorValue ?? entity.Name;

        // Build parent insert
        results.Add(BuildInsertQueryForTable(entity.ParentEntity, parentData, tenantId));

        // Build child insert
        results.Add(BuildInsertQueryForTable(entity, childData, null));

        return results;
    }

    /// <summary>
    /// Build UPDATE queries for a child entity in a table-per-type hierarchy.
    /// Returns two SQL statements: one for the parent table, one for the child table.
    /// </summary>
    public List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceUpdateQueries(
        BmEntity entity,
        Guid id,
        Dictionary<string, object?> data,
        Guid? tenantId = null)
    {
        if (entity.ParentEntity == null)
            return new() { _parent.BuildUpdateQuery(entity, id, data, tenantId) };

        var results = new List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)>();

        // Split data into parent fields and child fields
        var parentFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectAllFieldNames(entity.ParentEntity, parentFieldNames);

        var parentData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var childData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in data)
        {
            // Skip ID — it's a WHERE clause field, not a SET field
            if (kvp.Key.Equals("Id", StringComparison.OrdinalIgnoreCase))
                continue;

            if (parentFieldNames.Contains(kvp.Key))
            {
                parentData[kvp.Key] = kvp.Value;
            }
            else
            {
                childData[kvp.Key] = kvp.Value;
            }
        }

        // Build parent update (if there are parent fields to update)
        if (parentData.Count > 0)
        {
            results.Add(_parent.BuildUpdateQuery(entity.ParentEntity, id, parentData, tenantId));
        }

        // Build child update (if there are child fields to update)
        if (childData.Count > 0)
        {
            results.Add(_parent.BuildUpdateQuery(entity, id, childData, null));
        }

        // If neither had fields, still need at least one update (e.g. updated_at)
        if (results.Count == 0)
        {
            results.Add(_parent.BuildUpdateQuery(entity.ParentEntity, id, parentData, tenantId));
        }

        return results;
    }

    /// <summary>
    /// Build DELETE queries for a child entity in a table-per-type hierarchy.
    /// Returns two SQL statements: child first (FK dependency), then parent.
    /// </summary>
    public List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)> BuildInheritanceDeleteQueries(
        BmEntity entity,
        Guid id,
        Guid? tenantId = null,
        bool softDelete = false)
    {
        if (entity.ParentEntity == null)
            return new() { _parent.BuildDeleteQuery(entity, id, tenantId, softDelete) };

        var results = new List<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters)>();

        // Delete child row first (it has FK to parent)
        results.Add(_parent.BuildDeleteQuery(entity, id, null, softDelete));

        // Then delete parent row
        results.Add(_parent.BuildDeleteQuery(entity.ParentEntity, id, tenantId, softDelete));

        return results;
    }

    private (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) BuildInsertQueryForTable(
        BmEntity entity,
        Dictionary<string, object?> data,
        Guid? tenantId)
    {
        var parameters = new List<NpgsqlParameter>();
        var columns = new List<string>();
        var values = new List<string>();

        var fieldTypes = entity.Fields.ToDictionary(f => f.Name, f => f.TypeString ?? "", StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in data)
        {
            var columnName = kvp.Key == SchemaConstants.DiscriminatorColumn ? SchemaConstants.DiscriminatorColumn : NamingConvention.GetColumnName(kvp.Key);
            var paramName = $"@p{parameters.Count}";

            columns.Add(columnName);
            values.Add(paramName);

            fieldTypes.TryGetValue(kvp.Key, out var fieldType);
            parameters.Add(new NpgsqlParameter(paramName, DynamicSqlBuilder.ConvertValueTyped(kvp.Value, fieldType ?? "") ?? DBNull.Value));
        }

        if (entity.TenantScoped && tenantId.HasValue &&
            !data.ContainsKey("TenantId") && !data.ContainsKey("tenantId"))
        {
            var paramName = $"@p{parameters.Count}";
            columns.Add(SchemaConstants.TenantIdColumn);
            values.Add(paramName);
            parameters.Add(new NpgsqlParameter(paramName, tenantId.Value));
        }

        var tableName = _parent.GetTableName(entity);
        var sql = $"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)}) RETURNING *";

        return (sql, parameters);
    }

    private static void CollectAllFieldNames(BmEntity entity, HashSet<string> fieldNames)
    {
        foreach (var field in entity.Fields)
            fieldNames.Add(field.Name);
        if (entity.ParentEntity != null)
            CollectAllFieldNames(entity.ParentEntity, fieldNames);
    }
}
