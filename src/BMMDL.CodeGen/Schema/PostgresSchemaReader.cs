using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using BMMDL.CodeGen.Schema;

namespace BMMDL.CodeGen.Schema;

/// <summary>
/// Reads PostgreSQL schema from information_schema.
/// </summary>
public class PostgresSchemaReader
{
    private readonly string _connectionString;

    public PostgresSchemaReader(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Reads complete schema snapshot from database.
    /// </summary>
    public async Task<SchemaSnapshot> ReadSchemaAsync(string? schemaFilter = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var snapshot = new SchemaSnapshot
        {
            ReadAt = DateTime.UtcNow,
            SchemaName = schemaFilter ?? "public",
            DatabaseVersion = connection.ServerVersion
        };

        // Read all tables
        snapshot.Tables = await ReadTablesAsync(connection, schemaFilter);

        // Read details for each table
        foreach (var table in snapshot.Tables)
        {
            table.Columns = await ReadColumnsAsync(connection, table.Schema, table.Name);
            table.Indexes = await ReadIndexesAsync(connection, table.Schema, table.Name);
            table.Constraints = await ReadConstraintsAsync(connection, table.Schema, table.Name);
        }

        return snapshot;
    }

    private async Task<List<TableInfo>> ReadTablesAsync(NpgsqlConnection connection, string? schemaFilter)
    {
        var tables = new List<TableInfo>();
        
        var sql = @"
            SELECT table_schema, table_name
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
            AND table_schema NOT IN ('pg_catalog', 'information_schema')";

        if (!string.IsNullOrEmpty(schemaFilter))
        {
            sql += " AND table_schema = @schemaFilter";
        }

        sql += " ORDER BY table_schema, table_name";

        await using var cmd = new NpgsqlCommand(sql, connection);
        if (!string.IsNullOrEmpty(schemaFilter))
        {
            cmd.Parameters.AddWithValue("schemaFilter", schemaFilter);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(new TableInfo
            {
                Schema = reader.GetString(0),
                Name = reader.GetString(1)
            });
        }

        return tables;
    }

    private async Task<List<ColumnInfo>> ReadColumnsAsync(
        NpgsqlConnection connection, 
        string schema, 
        string tableName)
    {
        var columns = new List<ColumnInfo>();

        var sql = @"
            SELECT 
                column_name,
                data_type,
                is_nullable,
                column_default,
                character_maximum_length,
                numeric_precision,
                numeric_scale,
                ordinal_position,
                is_generated,
                generation_expression
            FROM information_schema.columns
            WHERE table_schema = @schema
            AND table_name = @tableName
            ORDER BY ordinal_position";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var column = new ColumnInfo
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                IsNullable = reader.GetString(2) == "YES",
                DefaultValue = reader.IsDBNull(3) ? null : reader.GetString(3),
                MaxLength = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                NumericPrecision = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                NumericScale = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                OrdinalPosition = reader.GetInt32(7),
                IsGenerated = reader.GetString(8) != "NEVER",
                GenerationExpression = reader.IsDBNull(9) ? null : reader.GetString(9)
            };

            columns.Add(column);
        }

        return columns;
    }

    private async Task<List<IndexInfo>> ReadIndexesAsync(
        NpgsqlConnection connection,
        string schema,
        string tableName)
    {
        var indexes = new List<IndexInfo>();

        var sql = @"
            SELECT
                i.indexname,
                i.indexdef,
                ix.indisunique,
                ix.indisprimary,
                ARRAY_AGG(a.attname ORDER BY array_position(ix.indkey, a.attnum)) as columns
            FROM pg_indexes i
            JOIN pg_class t ON t.relname = i.tablename
            JOIN pg_namespace n ON n.nspname = i.schemaname AND t.relnamespace = n.oid
            JOIN pg_index ix ON ix.indexrelid = (
                SELECT oid FROM pg_class WHERE relname = i.indexname AND relnamespace = n.oid
            )
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
            WHERE i.schemaname = @schema
            AND i.tablename = @tableName
            GROUP BY i.indexname, i.indexdef, ix.indisunique, ix.indisprimary";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var index = new IndexInfo
            {
                Name = reader.GetString(0),
                Definition = reader.IsDBNull(1) ? null : reader.GetString(1),
                IsUnique = reader.GetBoolean(2),
                IsPrimary = reader.GetBoolean(3),
                Columns = ((string[])reader.GetValue(4)).ToList()
            };

            indexes.Add(index);
        }

        return indexes;
    }

    private async Task<List<ConstraintInfo>> ReadConstraintsAsync(
        NpgsqlConnection connection,
        string schema,
        string tableName)
    {
        var constraints = new List<ConstraintInfo>();

        // Read all constraint types
        constraints.AddRange(await ReadPrimaryKeyConstraintsAsync(connection, schema, tableName));
        constraints.AddRange(await ReadForeignKeyConstraintsAsync(connection, schema, tableName));
        constraints.AddRange(await ReadUniqueConstraintsAsync(connection, schema, tableName));
        constraints.AddRange(await ReadCheckConstraintsAsync(connection, schema, tableName));

        return constraints;
    }

    private async Task<List<ConstraintInfo>> ReadPrimaryKeyConstraintsAsync(
        NpgsqlConnection connection,
        string schema,
        string tableName)
    {
        var constraints = new List<ConstraintInfo>();

        var sql = @"
            SELECT 
                tc.constraint_name,
                ARRAY_AGG(kcu.column_name ORDER BY kcu.ordinal_position) as columns
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu 
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY'
            AND tc.table_schema = @schema
            AND tc.table_name = @tableName
            GROUP BY tc.constraint_name";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            constraints.Add(new ConstraintInfo
            {
                Name = reader.GetString(0),
                Type = ConstraintType.PrimaryKey,
                Columns = ((string[])reader.GetValue(1)).ToList()
            });
        }

        return constraints;
    }

    private async Task<List<ConstraintInfo>> ReadForeignKeyConstraintsAsync(
        NpgsqlConnection connection,
        string schema,
        string tableName)
    {
        var constraints = new List<ConstraintInfo>();

        var sql = @"
            SELECT
                tc.constraint_name,
                ARRAY_AGG(kcu.column_name ORDER BY kcu.ordinal_position) as columns,
                ccu.table_name AS foreign_table_name,
                ARRAY_AGG(ccu.column_name ORDER BY kcu.ordinal_position) as foreign_columns
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
                ON ccu.constraint_name = tc.constraint_name
                AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
            AND tc.table_schema = @schema
            AND tc.table_name = @tableName
            GROUP BY tc.constraint_name, ccu.table_name";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            constraints.Add(new ConstraintInfo
            {
                Name = reader.GetString(0),
                Type = ConstraintType.ForeignKey,
                Columns = ((string[])reader.GetValue(1)).ToList(),
                ReferencedTable = reader.GetString(2),
                ReferencedColumns = ((string[])reader.GetValue(3)).ToList()
            });
        }

        return constraints;
    }

    private async Task<List<ConstraintInfo>> ReadUniqueConstraintsAsync(
        NpgsqlConnection connection,
        string schema,
        string tableName)
    {
        var constraints = new List<ConstraintInfo>();

        var sql = @"
            SELECT 
                tc.constraint_name,
                ARRAY_AGG(kcu.column_name ORDER BY kcu.ordinal_position) as columns
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu 
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'UNIQUE'
            AND tc.table_schema = @schema
            AND tc.table_name = @tableName
            GROUP BY tc.constraint_name";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            constraints.Add(new ConstraintInfo
            {
                Name = reader.GetString(0),
                Type = ConstraintType.Unique,
                Columns = ((string[])reader.GetValue(1)).ToList()
            });
        }

        return constraints;
    }

    private async Task<List<ConstraintInfo>> ReadCheckConstraintsAsync(
        NpgsqlConnection connection,
        string schema,
        string tableName)
    {
        var constraints = new List<ConstraintInfo>();

        var sql = @"
            SELECT 
                cc.constraint_name,
                cc.check_clause
            FROM information_schema.check_constraints cc
            JOIN information_schema.table_constraints tc
                ON cc.constraint_name = tc.constraint_name
                AND cc.constraint_schema = tc.constraint_schema
            WHERE tc.table_schema = @schema
            AND tc.table_name = @tableName";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("tableName", tableName);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            constraints.Add(new ConstraintInfo
            {
                Name = reader.GetString(0),
                Type = ConstraintType.Check,
                CheckClause = reader.GetString(1),
                Columns = new List<string>() // Check constraints don't have specific columns
            });
        }

        return constraints;
    }
}
