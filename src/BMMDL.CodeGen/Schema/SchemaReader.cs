using Npgsql;

namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Reads database schema structure from PostgreSQL information_schema.
/// Produces a SchemaSnapshot for comparison with the compiled model.
/// </summary>
public class SchemaReader
{
    /// <summary>
    /// Read schema structure from a live PostgreSQL database.
    /// </summary>
    public async Task<SchemaSnapshot> ReadSchemaAsync(string connectionString, string schemaName)
    {
        var snapshot = new SchemaSnapshot
        {
            SchemaName = schemaName,
            ReadAt = DateTime.UtcNow
        };

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Read tables
        var tables = await ReadTablesAsync(conn, schemaName);

        foreach (var tableName in tables)
        {
            var tableInfo = new TableInfo
            {
                Schema = schemaName,
                Name = tableName
            };

            // Read columns for each table
            tableInfo.Columns = await ReadColumnsAsync(conn, schemaName, tableName);

            // Read constraints
            tableInfo.Constraints = await ReadConstraintsAsync(conn, schemaName, tableName);

            snapshot.Tables.Add(tableInfo);
        }

        return snapshot;
    }

    private async Task<List<string>> ReadTablesAsync(NpgsqlConnection conn, string schemaName)
    {
        var tables = new List<string>();

        await using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = @schema AND table_type = 'BASE TABLE' " +
            "ORDER BY table_name", conn);
        cmd.Parameters.AddWithValue("@schema", schemaName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private async Task<List<ColumnInfo>> ReadColumnsAsync(
        NpgsqlConnection conn, string schemaName, string tableName)
    {
        var columns = new List<ColumnInfo>();

        await using var cmd = new NpgsqlCommand(
            "SELECT column_name, data_type, is_nullable, column_default, " +
            "character_maximum_length, numeric_precision, numeric_scale, ordinal_position, " +
            "is_generated, generation_expression " +
            "FROM information_schema.columns " +
            "WHERE table_schema = @schema AND table_name = @table " +
            "ORDER BY ordinal_position", conn);
        cmd.Parameters.AddWithValue("@schema", schemaName);
        cmd.Parameters.AddWithValue("@table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnInfo
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                IsNullable = reader.GetString(2) == "YES",
                DefaultValue = reader.IsDBNull(3) ? null : reader.GetString(3),
                MaxLength = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                NumericPrecision = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                NumericScale = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                OrdinalPosition = reader.GetInt32(7),
                IsGenerated = !reader.IsDBNull(8) && reader.GetString(8) != "NEVER",
                GenerationExpression = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return columns;
    }

    private async Task<List<ConstraintInfo>> ReadConstraintsAsync(
        NpgsqlConnection conn, string schemaName, string tableName)
    {
        var constraints = new List<ConstraintInfo>();

        await using var cmd = new NpgsqlCommand(
            "SELECT tc.constraint_name, tc.constraint_type, " +
            "kcu.column_name, ccu.table_name AS ref_table, ccu.column_name AS ref_column " +
            "FROM information_schema.table_constraints tc " +
            "LEFT JOIN information_schema.key_column_usage kcu " +
            "  ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema " +
            "LEFT JOIN information_schema.constraint_column_usage ccu " +
            "  ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema " +
            "WHERE tc.table_schema = @schema AND tc.table_name = @table " +
            "ORDER BY tc.constraint_name, kcu.ordinal_position", conn);
        cmd.Parameters.AddWithValue("@schema", schemaName);
        cmd.Parameters.AddWithValue("@table", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        var constraintMap = new Dictionary<string, ConstraintInfo>();

        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            var column = reader.IsDBNull(2) ? null : reader.GetString(2);
            var refTable = reader.IsDBNull(3) ? null : reader.GetString(3);
            var refColumn = reader.IsDBNull(4) ? null : reader.GetString(4);

            if (!constraintMap.TryGetValue(name, out var constraint))
            {
                constraint = new ConstraintInfo
                {
                    Name = name,
                    Type = type switch
                    {
                        "PRIMARY KEY" => ConstraintType.PrimaryKey,
                        "FOREIGN KEY" => ConstraintType.ForeignKey,
                        "UNIQUE" => ConstraintType.Unique,
                        "CHECK" => ConstraintType.Check,
                        _ => ConstraintType.Check
                    },
                    ReferencedTable = refTable,
                    ReferencedColumns = new List<string>()
                };
                constraintMap[name] = constraint;
            }

            if (column != null)
                constraint.Columns.Add(column);
            if (refColumn != null && constraint.Type == ConstraintType.ForeignKey)
                constraint.ReferencedColumns?.Add(refColumn);
        }

        constraints.AddRange(constraintMap.Values);
        return constraints;
    }
}
