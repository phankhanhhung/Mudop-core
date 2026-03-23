using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Generators;

/// <summary>
/// Generates FileReference column expansions and storage constraints.
/// </summary>
internal class FileReferenceDdlGenerator
{
    /// <summary>
    /// Generate metadata columns for a FileReference field.
    /// Expands {fieldName}: FileReference into 8 metadata columns.
    /// </summary>
    public List<string> GenerateFileReferenceColumns(BmField field, BmFileReferenceType fileRefType)
    {
        var columnName = NamingConvention.GetColumnName(field.Name);
        var nullClause = fileRefType.IsNullable ? "" : " NOT NULL";

        return new List<string>
        {
            $"{NamingConvention.QuoteIdentifier($"{columnName}_provider")} VARCHAR(50){nullClause}",
            $"{NamingConvention.QuoteIdentifier($"{columnName}_bucket")} VARCHAR(255){nullClause}",
            $"{NamingConvention.QuoteIdentifier($"{columnName}_key")} VARCHAR(1024){nullClause}",
            $"{NamingConvention.QuoteIdentifier($"{columnName}_size")} BIGINT{nullClause}",
            $"{NamingConvention.QuoteIdentifier($"{columnName}_mime_type")} VARCHAR(255){nullClause}",
            $"{NamingConvention.QuoteIdentifier($"{columnName}_checksum")} VARCHAR(128){nullClause}",
            $"{NamingConvention.QuoteIdentifier($"{columnName}_uploaded_at")} TIMESTAMPTZ{nullClause}",
            $"{NamingConvention.QuoteIdentifier($"{columnName}_uploaded_by")} UUID{nullClause}"
        };
    }

    /// <summary>
    /// Generate CHECK constraints for FileReference storage configuration.
    /// </summary>
    public List<string> GenerateFileReferenceConstraints(BmField field, BmFileReferenceType fileRefType)
    {
        var columnName = NamingConvention.GetColumnName(field.Name);
        var constraints = new List<string>();

        if (fileRefType.MaxSizeBytes.HasValue)
        {
            constraints.Add(
                $"CONSTRAINT {NamingConvention.QuoteIdentifier($"chk_{columnName}_max_size")} CHECK ({NamingConvention.QuoteIdentifier($"{columnName}_size")} <= {fileRefType.MaxSizeBytes.Value})"
            );
        }

        if (fileRefType.AllowedMimeTypes != null && fileRefType.AllowedMimeTypes.Count > 0)
        {
            var mimeTypeList = string.Join(", ", fileRefType.AllowedMimeTypes.Select(m => $"'{m.Replace("'", "''")}'"));
            constraints.Add(
                $"CONSTRAINT {NamingConvention.QuoteIdentifier($"chk_{columnName}_mime_type")} CHECK ({NamingConvention.QuoteIdentifier($"{columnName}_mime_type")} IN ({mimeTypeList}))"
            );
        }

        if (!string.IsNullOrWhiteSpace(fileRefType.Provider))
        {
            var escapedProvider = fileRefType.Provider.Replace("'", "''");
            constraints.Add(
                $"CONSTRAINT {NamingConvention.QuoteIdentifier($"chk_{columnName}_provider")} CHECK ({NamingConvention.QuoteIdentifier($"{columnName}_provider")} = '{escapedProvider}')"
            );
        }

        return constraints;
    }
}
