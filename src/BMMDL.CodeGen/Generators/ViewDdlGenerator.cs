using System.Text;
using System.Text.RegularExpressions;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Generators;

/// <summary>
/// Generates view DDL: CREATE VIEW, materialized views, cross-aspect views.
/// </summary>
internal class ViewDdlGenerator
{
    private readonly DdlGeneratorContext _ctx;

    public ViewDdlGenerator(DdlGeneratorContext context)
    {
        _ctx = context;
    }

    private static string Q(string id) => NamingConvention.QuoteIdentifier(id);

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
            return $"{Q(schema)}.{Q(name)}";
        }
        return Q(qualifiedName);
    }

    /// <summary>
    /// Generate CREATE VIEW statement for a single view.
    /// </summary>
    public string GenerateCreateView(BmView view)
    {
        var sb = new StringBuilder();
        var schemaName = NamingConvention.GetSchemaName(view.Namespace);
        var viewName = NamingConvention.GetColumnName(view.Name);
        var qualifiedName = string.IsNullOrEmpty(schemaName)
            ? Q(viewName)
            : $"{Q(schemaName)}.{Q(viewName)}";

        var isMaterialized = view.HasAnnotation("Materialized");

        string? selectSql = null;
        if (view.ParsedSelect != null)
        {
            var sqlGen = new Visitors.SelectStatementSqlGenerator(_ctx.Cache.Entities);
            selectSql = sqlGen.Generate(view.ParsedSelect);
        }
        else if (!string.IsNullOrWhiteSpace(view.SelectStatement))
        {
            selectSql = TransformViewSelectStatement(view.SelectStatement);
        }

        if (string.IsNullOrWhiteSpace(selectSql))
        {
            sb.AppendLine($"-- VIEW: {view.QualifiedName} (no SELECT statement defined, skipping CREATE VIEW)");
            return sb.ToString();
        }

        sb.AppendLine($"-- View: {view.QualifiedName}");

        if (isMaterialized)
        {
            sb.AppendLine($"CREATE MATERIALIZED VIEW {qualifiedName} AS");
        }
        else
        {
            sb.AppendLine($"CREATE OR REPLACE VIEW {qualifiedName} AS");
        }

        sb.AppendLine(selectSql);
        sb.AppendLine(";");

        if (isMaterialized)
        {
            // Determine a suitable column for the unique index required for REFRESH CONCURRENTLY.
            // Use 'id' only if the view's SELECT list includes it; otherwise skip the index.
            var hasIdColumn = false;
            if (view.ParsedSelect != null)
            {
                hasIdColumn = view.ParsedSelect.Columns.Any(c =>
                    c.IsWildcard ||
                    string.Equals(c.Alias, "id", StringComparison.OrdinalIgnoreCase) ||
                    (c.Alias == null && c.Expression is MetaModel.Expressions.BmIdentifierExpression idExpr &&
                     idExpr.Path.Count == 1 &&
                     string.Equals(idExpr.Path[0], "id", StringComparison.OrdinalIgnoreCase)) ||
                    (c.Alias == null && string.Equals(c.ExpressionString, "id", StringComparison.OrdinalIgnoreCase)));
            }
            else if (!string.IsNullOrWhiteSpace(view.SelectStatement))
            {
                // Heuristic: check if 'id' appears as a selected column in raw SQL
                hasIdColumn = System.Text.RegularExpressions.Regex.IsMatch(
                    view.SelectStatement, @"\bid\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            if (hasIdColumn)
            {
                sb.AppendLine();
                sb.AppendLine($"-- Unique index for concurrent refresh");
                sb.AppendLine($"CREATE UNIQUE INDEX {Q($"idx_{viewName}_refresh")} ON {qualifiedName}({Q("id")});");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine($"-- NOTE: No 'id' column detected in materialized view; skipping unique index.");
                sb.AppendLine($"-- Add a unique index manually if REFRESH CONCURRENTLY is needed.");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generate all views in the model.
    /// </summary>
    public string GenerateAllViews()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Views");
        sb.AppendLine("-- ============================================");
        sb.AppendLine();

        foreach (var view in _ctx.Cache.Views.Values)
        {
            sb.AppendLine(GenerateCreateView(view));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Transform DSL select statement to PostgreSQL SQL.
    /// Resolves entity names to table names.
    /// </summary>
    private string TransformViewSelectStatement(string selectStatement)
    {
        var sql = selectStatement;

        foreach (var entity in _ctx.Cache.Entities.Values)
        {
            var entityName = entity.Name;
            var qualifiedName = entity.QualifiedName;
            var tableName = QuoteQualifiedName(_ctx.GetQualifiedTableNameForEntity(entity));

            if (!string.IsNullOrEmpty(qualifiedName))
            {
                sql = Regex.Replace(
                    sql,
                    $@"\b{Regex.Escape(qualifiedName)}\b",
                    tableName,
                    RegexOptions.IgnoreCase);
            }

            sql = Regex.Replace(
                sql,
                $@"\b{entityName}\b",
                tableName,
                RegexOptions.IgnoreCase);
        }

        var fieldPattern = @"\b([A-Z][a-z]+[A-Z][a-zA-Z]*)\b";
        sql = Regex.Replace(sql, fieldPattern, match =>
        {
            var fieldName = match.Value;
            var sqlKeywords = new[] { "SELECT", "FROM", "WHERE", "AND", "OR", "AS", "ON", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "GROUP", "ORDER", "BY", "HAVING" };
            if (sqlKeywords.Contains(fieldName.ToUpper()))
                return fieldName;
            return Q(NamingConvention.GetColumnName(fieldName));
        });

        return sql;
    }

    /// <summary>
    /// Generate UNION ALL views for aspects annotated with @Query.CrossAspect.
    /// </summary>
    public string GenerateCrossAspectViews()
    {
        var sb = new StringBuilder();

        foreach (var aspect in _ctx.Cache.Aspects.Values)
        {
            if (!aspect.HasAnnotation("Query.CrossAspect"))
                continue;

            var entitiesWithAspect = _ctx.Cache.Entities.Values
                .Where(e => e.Aspects.Any(a =>
                    string.Equals(a, aspect.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, aspect.QualifiedName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (entitiesWithAspect.Count == 0)
                continue;

            var viewName = NamingConvention.GetColumnName(aspect.Name) + "_cross_view";
            var schema = !string.IsNullOrEmpty(aspect.Namespace)
                ? NamingConvention.GetSchemaName(aspect.Namespace)
                : "public";
            var qualifiedViewName = $"{Q(schema)}.{Q(viewName)}";

            var aspectColumns = aspect.Fields
                .Where(f => !f.IsVirtual)
                .Select(f => NamingConvention.GetColumnName(f.Name))
                .ToList();

            if (aspectColumns.Count == 0)
                continue;

            var selectColumns = new List<string> { Q("id") };
            selectColumns.AddRange(aspectColumns.Select(Q));
            var columnList = string.Join(", ", selectColumns);

            sb.AppendLine($"-- Cross-aspect view for {aspect.QualifiedName}");
            sb.AppendLine($"CREATE OR REPLACE VIEW {qualifiedViewName} AS");

            var unionParts = new List<string>();
            foreach (var entity in entitiesWithAspect)
            {
                var tableName = QuoteQualifiedName(_ctx.GetQualifiedTableNameForEntity(entity));
                var entitySelect = $"SELECT {columnList}, '{entity.Name}' AS {Q("_source_entity")} FROM {tableName}";
                unionParts.Add(entitySelect);
            }

            sb.AppendLine(string.Join("\nUNION ALL\n", unionParts));
            sb.AppendLine(";");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
