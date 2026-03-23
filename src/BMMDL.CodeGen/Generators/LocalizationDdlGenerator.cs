using System.Text;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Generators;

/// <summary>
/// Generates localization DDL: _texts companion tables and sync triggers.
/// </summary>
internal class LocalizationDdlGenerator
{
    private readonly DdlGeneratorContext _ctx;

    public LocalizationDdlGenerator(DdlGeneratorContext context)
    {
        _ctx = context;
    }

    /// <summary>
    /// Generate a companion _texts table for entities with localized fields.
    /// Returns null if entity has no localized fields.
    /// </summary>
    public string? GenerateTextsTable(BmEntity entity)
    {
        string Q(string id) => NamingConvention.QuoteIdentifier(id);

        var localizedFields = entity.Fields
            .Where(f => f.TypeRef is BmLocalizedType)
            .ToList();

        if (localizedFields.Count == 0)
            return null;

        var unqualifiedParent = _ctx.GetUnqualifiedTableName(entity);
        var textsTableName = unqualifiedParent + "_texts";

        var moduleName = _ctx.GetModuleNameForEntity(entity);
        var schema = !string.IsNullOrEmpty(entity.Namespace)
            ? NamingConvention.GetSchemaName(entity.Namespace)
            : (!string.IsNullOrEmpty(moduleName) ? NamingConvention.GetSchemaName(moduleName) : null);
        var qualifiedTexts = schema != null ? $"{Q(schema)}.{Q(textsTableName)}" : Q(textsTableName);

        var sb = new StringBuilder();
        sb.AppendLine($"-- Localized texts table for {entity.Name}");
        sb.AppendLine($"CREATE TABLE {qualifiedTexts} (");

        var columns = new List<string>();
        columns.Add($"    {Q("id")} UUID NOT NULL");
        columns.Add($"    {Q("locale")} VARCHAR(14) NOT NULL");

        if (entity.TenantScoped)
        {
            columns.Add($"    {Q("tenant_id")} UUID NOT NULL");
        }

        foreach (var field in localizedFields)
        {
            var localizedType = (BmLocalizedType)field.TypeRef!;
            var innerField = new BmField
            {
                Name = field.Name,
                TypeString = localizedType.InnerType.ToTypeString(),
                TypeRef = localizedType.InnerType,
                IsNullable = true
            };
            var resolved = _ctx.Resolver.Resolve(innerField);
            var columnName = NamingConvention.GetColumnName(field.Name);
            columns.Add($"    {Q(columnName)} {resolved.PostgresType}");
        }

        var constraints = new List<string>();
        constraints.Add($"    PRIMARY KEY ({Q("id")}, {Q("locale")})");

        sb.AppendLine(string.Join(",\n", columns.Concat(constraints)));
        sb.AppendLine(");");

        // Generate sync trigger
        var parentTableName = QuoteQualifiedName(_ctx.GetQualifiedTableNameForEntity(entity));
        var functionName = $"{unqualifiedParent}_texts_sync";
        var triggerName = $"tr_{unqualifiedParent}_texts_sync";

        var localizedColumnNames = localizedFields
            .Select(f => NamingConvention.GetColumnName(f.Name))
            .ToList();

        sb.AppendLine();
        sb.AppendLine($"-- Trigger: sync default-language text on INSERT/UPDATE of parent entity");
        sb.AppendLine($"CREATE OR REPLACE FUNCTION {Q(functionName)}()");
        sb.AppendLine("RETURNS TRIGGER AS $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    INSERT INTO {qualifiedTexts} ({Q("id")}, {Q("locale")}, {string.Join(", ", localizedColumnNames.Select(Q))})");
        sb.AppendLine($"    VALUES (NEW.{Q("id")}, 'default', {string.Join(", ", localizedColumnNames.Select(c => $"NEW.{Q(c)}"))})");
        sb.AppendLine($"    ON CONFLICT ({Q("id")}, {Q("locale")}) DO UPDATE SET");
        sb.AppendLine($"        {string.Join(",\n        ", localizedColumnNames.Select(c => $"{Q(c)} = EXCLUDED.{Q(c)}"))};");
        sb.AppendLine("    RETURN NEW;");
        sb.AppendLine("END;");
        sb.AppendLine("$$ LANGUAGE plpgsql;");
        sb.AppendLine();
        sb.AppendLine($"DROP TRIGGER IF EXISTS {Q(triggerName)} ON {parentTableName};");
        sb.AppendLine($"CREATE TRIGGER {Q(triggerName)}");
        sb.AppendLine($"    AFTER INSERT OR UPDATE OF {string.Join(", ", localizedColumnNames.Select(Q))} ON {parentTableName}");
        sb.AppendLine($"    FOR EACH ROW EXECUTE FUNCTION {Q(functionName)}();");

        return sb.ToString();
    }

    /// <summary>
    /// Quote a potentially qualified name (schema.table → "schema"."table", or table → "table").
    /// </summary>
    private static string QuoteQualifiedName(string qualifiedName)
    {
        var dotIndex = qualifiedName.IndexOf('.');
        if (dotIndex >= 0)
        {
            var schema = qualifiedName[..dotIndex];
            var name = qualifiedName[(dotIndex + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(name)}";
        }
        return NamingConvention.QuoteIdentifier(qualifiedName);
    }
}
