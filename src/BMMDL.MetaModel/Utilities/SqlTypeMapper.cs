using System;

namespace BMMDL.MetaModel.Utilities;

/// <summary>
/// Single source of truth for mapping BMMDL types to PostgreSQL SQL types.
/// Used by PgSqlActionGenerator, SyncTriggerGenerator, PostgresSchemaManager, and others.
/// </summary>
public static class SqlTypeMapper
{
    /// <summary>
    /// Map a BMMDL type string to its PostgreSQL SQL type equivalent.
    /// Handles parameterized types like String(n) and Decimal(p,s).
    /// Case-insensitive matching on the base type name.
    /// </summary>
    public static string MapToSqlType(string bmType)
    {
        if (string.IsNullOrEmpty(bmType))
            return "TEXT";

        var trimmed = bmType.Replace("?", "").Trim();

        // Handle parameterized types first
        if (trimmed.StartsWith("String(", StringComparison.OrdinalIgnoreCase))
            return $"VARCHAR({ExtractLength(trimmed)})";
        if (trimmed.StartsWith("Decimal(", StringComparison.OrdinalIgnoreCase))
            return $"NUMERIC({ExtractDecimalParams(trimmed)})";

        return trimmed.ToLowerInvariant() switch
        {
            "integer" => "INTEGER",
            "biginteger" or "int64" => "BIGINT",
            "decimal" => "NUMERIC",
            "boolean" => "BOOLEAN",
            "string" => "TEXT",
            "datetime" => "TIMESTAMP",
            "timestamp" => "TIMESTAMPTZ",
            "date" => "DATE",
            "time" => "TIME",
            "uuid" => "UUID",
            "binary" => "BYTEA",
            "void" => "void",
            _ => "TEXT"
        };
    }

    /// <summary>
    /// Extract the length parameter from a parameterized String type.
    /// e.g., "String(100)" → 100. Returns 255 as default.
    /// </summary>
    public static int ExtractLength(string typeSpec)
    {
        var start = typeSpec.IndexOf('(') + 1;
        var end = typeSpec.IndexOf(')');
        if (start <= 0 || end <= start) return 255;
        return int.TryParse(typeSpec[start..end], out var len) ? len : 255;
    }

    /// <summary>
    /// Extract the precision and scale parameters from a parameterized Decimal type as a string.
    /// e.g., "Decimal(18,2)" → "18,2". Returns "18,2" as default.
    /// </summary>
    public static string ExtractDecimalParams(string typeSpec)
    {
        var start = typeSpec.IndexOf('(') + 1;
        var end = typeSpec.IndexOf(')');
        if (start <= 0 || end <= start) return "18,2";
        return typeSpec[start..end];
    }

    /// <summary>
    /// Extract the precision and scale parameters from a parameterized Decimal type as a tuple.
    /// e.g., "Decimal(18,2)" → (18, 2). Returns (10, 2) as default.
    /// </summary>
    public static (int precision, int scale) ExtractDecimalParamsTuple(string typeSpec)
    {
        var raw = ExtractDecimalParams(typeSpec);
        var parts = raw.Split(',');
        var precision = int.TryParse(parts[0].Trim(), out var p) ? p : 10;
        var scale = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var s) ? s : 2;
        return (precision, scale);
    }

    /// <summary>
    /// Escape a string value for safe inclusion in a PostgreSQL SQL literal.
    /// Doubles single quotes to prevent SQL injection in DDL/DML string interpolation.
    /// e.g., "O'Brien" → "O''Brien"
    /// </summary>
    public static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''");
}
