namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Utilities;
using Npgsql;

/// <summary>
/// Static utility for reading NpgsqlDataReader rows into dictionaries.
/// Converts snake_case column names to PascalCase keys.
/// </summary>
public static class RowReader
{
    /// <summary>
    /// Read a row from the reader into a case-insensitive dictionary.
    /// Converts snake_case column names to PascalCase property names.
    /// </summary>
    public static Dictionary<string, object?> ReadRow(NpgsqlDataReader reader)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
            var propertyName = NamingConvention.ToPascalCase(columnName);
            result[propertyName] = value;
        }

        return result;
    }
}
