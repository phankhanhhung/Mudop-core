using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen;

/// <summary>
/// Represents a flattened field from a structured type
/// </summary>
public class FlattenedField
{
    /// <summary>Column name with prefix (e.g., "home_address_street")</summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>PostgreSQL type</summary>
    public string PostgresType { get; set; } = string.Empty;

    /// <summary>Whether nullable</summary>
    public bool Nullable { get; set; } = true;

    /// <summary>Default value</summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Generate column definition
    /// </summary>
    public string ToColumnDefinition()
    {
        var parts = new List<string>
        {
            NamingConvention.QuoteIdentifier(ColumnName),
            PostgresType
        };

        if (!Nullable)
        {
            parts.Add("NOT NULL");
        }

        if (DefaultValue != null)
        {
            parts.Add($"DEFAULT {DefaultValue}");
        }

        return string.Join(" ", parts);
    }
}
