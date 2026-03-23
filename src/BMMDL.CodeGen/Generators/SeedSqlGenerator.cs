using System.Globalization;
using System.Text;
using BMMDL.MetaModel;
using BMMDL.MetaModel.Expressions;
using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;

namespace BMMDL.CodeGen.Generators;

/// <summary>
/// Generates INSERT SQL statements from BmSeedDef objects.
/// Produces idempotent INSERT ... ON CONFLICT DO NOTHING statements.
/// </summary>
internal class SeedSqlGenerator
{
    private readonly DdlGeneratorContext _ctx;

    public SeedSqlGenerator(DdlGeneratorContext context)
    {
        _ctx = context;
    }

    /// <summary>
    /// Generate INSERT SQL for all seed definitions in the model.
    /// Seeds are sorted by entity FK dependencies so that referenced rows are inserted first.
    /// </summary>
    public string GenerateAllSeedSql(BmModel model)
    {
        if (model.Seeds.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        var sortedSeeds = SortSeedsByDependency(model);

        foreach (var seed in sortedSeeds)
        {
            var entity = ResolveEntity(seed, model);
            if (entity == null)
            {
                sb.AppendLine($"-- WARNING: Could not resolve entity '{seed.EntityName}' for seed '{seed.Name}'. Skipping.");
                sb.AppendLine();
                continue;
            }

            var seedSql = GenerateSeedSql(seed, entity);
            if (!string.IsNullOrWhiteSpace(seedSql))
            {
                sb.Append(seedSql);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generate INSERT SQL for a single seed definition.
    /// </summary>
    public string GenerateSeedSql(BmSeedDef seed, BmEntity entity)
    {
        if (seed.Rows.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        // Source location comment
        if (!string.IsNullOrEmpty(seed.SourceFile))
        {
            sb.AppendLine($"-- Seed: {seed.Name} (source: {Path.GetFileName(seed.SourceFile)}:{seed.StartLine})");
        }
        else
        {
            sb.AppendLine($"-- Seed: {seed.Name}");
        }

        // Determine qualified table name
        var qualifiedTableName = GetQualifiedTableName(entity);

        // Convert column names to snake_case
        var snakeColumns = seed.Columns
            .Select(c => NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(c)))
            .ToList();

        // Find primary key columns for ON CONFLICT clause
        var pkColumns = GetPrimaryKeyColumns(seed, entity);

        // Build INSERT statement
        sb.Append($"INSERT INTO {qualifiedTableName} (");
        sb.Append(string.Join(", ", snakeColumns));
        sb.AppendLine(")");
        sb.AppendLine("VALUES");

        // Generate value rows
        for (int i = 0; i < seed.Rows.Count; i++)
        {
            var row = seed.Rows[i];
            var values = new List<string>();

            for (int j = 0; j < row.Values.Count; j++)
            {
                values.Add(ConvertExpressionToSqlValue(row.Values[j]));
            }

            sb.Append($"    ({string.Join(", ", values)})");
            sb.AppendLine(i < seed.Rows.Count - 1 ? "," : "");
        }

        // ON CONFLICT clause
        if (pkColumns.Count > 0)
        {
            var quotedPkColumns = pkColumns
                .Select(c => NamingConvention.QuoteIdentifier(NamingConvention.ToSnakeCase(c)))
                .ToList();
            sb.AppendLine($"ON CONFLICT ({string.Join(", ", quotedPkColumns)}) DO NOTHING;");
        }
        else
        {
            // No PK found — emit plain INSERT without conflict handling
            sb.AppendLine(";");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Convert a BmExpression to a SQL literal value for INSERT.
    /// </summary>
    private string ConvertExpressionToSqlValue(BmExpression expression)
    {
        return expression switch
        {
            BmLiteralExpression lit => ConvertLiteral(lit),
            BmFunctionCallExpression func => ConvertFunctionCall(func),
            BmContextVariableExpression ctx => ConvertContextVariable(ctx),
            // For any other expression type, try the expression visitor as fallback
            _ => ConvertFallback(expression)
        };
    }

    /// <summary>
    /// Convert a literal expression to SQL value.
    /// </summary>
    private static string ConvertLiteral(BmLiteralExpression literal)
    {
        return literal.Kind switch
        {
            BmLiteralKind.String => EscapeSqlString(literal.Value?.ToString() ?? ""),
            BmLiteralKind.Integer => literal.Value?.ToString() ?? "0",
            BmLiteralKind.Decimal => FormatDecimal(literal.Value),
            BmLiteralKind.Boolean => literal.Value is true ? "true" : "false",
            BmLiteralKind.Null => "NULL",
            BmLiteralKind.EnumValue => EscapeSqlString(literal.Value?.ToString() ?? ""),
            _ => "NULL"
        };
    }

    /// <summary>
    /// Convert a function call expression to SQL value.
    /// Handles UUID('...') → '...'::uuid, and other common seed-time functions.
    /// </summary>
    private string ConvertFunctionCall(BmFunctionCallExpression func)
    {
        var funcName = func.FunctionName.ToUpperInvariant();

        switch (funcName)
        {
            case "UUID" when func.Arguments.Count == 1 && func.Arguments[0] is BmLiteralExpression uuidLit:
                // UUID('some-uuid-string') → 'some-uuid-string'::uuid
                var uuidValue = uuidLit.Value?.ToString() ?? "";
                return $"{EscapeSqlString(uuidValue)}::uuid";

            case "NOW":
                return "now()";

            case "CURRENT_DATE":
                return "CURRENT_DATE";

            case "CURRENT_TIMESTAMP":
                return "CURRENT_TIMESTAMP";

            default:
                // General function call: translate arguments recursively
                var args = func.Arguments
                    .Select(ConvertExpressionToSqlValue)
                    .ToArray();
                return $"{funcName}({string.Join(", ", args)})";
        }
    }

    /// <summary>
    /// Convert a context variable expression to SQL value.
    /// </summary>
    private static string ConvertContextVariable(BmContextVariableExpression ctx)
    {
        if (ctx.Path.Count == 0)
            return "NULL";

        var varName = ctx.Path[0].ToLowerInvariant();

        return varName switch
        {
            "now" => "now()",
            "today" => "CURRENT_DATE",
            "user" => "current_setting('app.user_id', true)",
            "tenant" => "current_setting('app.tenant_id', true)",
            _ => $"current_setting('app.{NamingConvention.ToSnakeCase(varName)}', true)"
        };
    }

    /// <summary>
    /// Fallback conversion for expressions not directly handled.
    /// Attempts to use PostgresSqlExpressionVisitor if entity context is available.
    /// </summary>
    private string ConvertFallback(BmExpression expression)
    {
        // Use the expression's own string representation as a last resort.
        // For seed data, the compiler should have validated that only
        // literal-like expressions appear, so this path is rarely hit.
        return expression.ToExpressionString();
    }

    /// <summary>
    /// Sort seeds by entity FK dependencies.
    /// If entity A has a FK to entity B, B's seeds come before A's seeds.
    /// </summary>
    private List<BmSeedDef> SortSeedsByDependency(BmModel model)
    {
        // Build a map from entity name to its seed definitions
        var entitySeeds = new Dictionary<string, List<BmSeedDef>>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in model.Seeds)
        {
            var entityName = seed.EntityName;
            if (!entitySeeds.ContainsKey(entityName))
                entitySeeds[entityName] = new List<BmSeedDef>();
            entitySeeds[entityName].Add(seed);
        }

        // Build dependency graph: entity name → set of entity names it depends on (via FK)
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityName in entitySeeds.Keys)
        {
            var entity = model.Entities.FirstOrDefault(e =>
                e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase) ||
                e.QualifiedName.Equals(entityName, StringComparison.OrdinalIgnoreCase));

            var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entity != null)
            {
                foreach (var assoc in entity.Associations)
                {
                    if (entitySeeds.ContainsKey(assoc.TargetEntity))
                    {
                        deps.Add(assoc.TargetEntity);
                    }
                }
            }

            dependencies[entityName] = deps;
        }

        // Topological sort (Kahn's algorithm)
        // in-degree[X] = number of dependencies X has (that also have seeds)
        var sorted = new List<string>();
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in entitySeeds.Keys)
        {
            inDegree[key] = dependencies.TryGetValue(key, out var deps) ? deps.Count : 0;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key).OrderBy(n => n));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(current);

            // Find entities that depend on current and reduce their in-degree
            foreach (var (entityName, deps) in dependencies)
            {
                if (deps.Contains(current))
                {
                    inDegree[entityName]--;
                    if (inDegree[entityName] == 0)
                        queue.Enqueue(entityName);
                }
            }
        }

        // If there are remaining entities (circular deps), add them at the end
        foreach (var entityName in entitySeeds.Keys.OrderBy(n => n))
        {
            if (!sorted.Contains(entityName))
                sorted.Add(entityName);
        }

        // Flatten: for each sorted entity, emit its seeds in declaration order
        var result = new List<BmSeedDef>();
        foreach (var entityName in sorted)
        {
            if (entitySeeds.TryGetValue(entityName, out var seeds))
                result.AddRange(seeds);
        }

        return result;
    }

    /// <summary>
    /// Resolve entity by seed's EntityName, checking both simple and qualified names.
    /// </summary>
    private BmEntity? ResolveEntity(BmSeedDef seed, BmModel model)
    {
        // Try exact match on entity name or qualified name
        var entity = model.Entities.FirstOrDefault(e =>
            e.Name.Equals(seed.EntityName, StringComparison.OrdinalIgnoreCase) ||
            e.QualifiedName.Equals(seed.EntityName, StringComparison.OrdinalIgnoreCase));

        if (entity != null)
            return entity;

        // Try with seed's namespace prefix
        if (!string.IsNullOrEmpty(seed.Namespace))
        {
            var qualifiedName = $"{seed.Namespace}.{seed.EntityName}";
            entity = model.Entities.FirstOrDefault(e =>
                e.QualifiedName.Equals(qualifiedName, StringComparison.OrdinalIgnoreCase));
        }

        if (entity != null)
            return entity;

        // Try cache lookup
        if (_ctx.Cache.Entities.TryGetValue(seed.EntityName, out var cachedEntity))
            return cachedEntity;

        return null;
    }

    /// <summary>
    /// Get the qualified table name for an entity, properly quoted.
    /// </summary>
    private string GetQualifiedTableName(BmEntity entity)
    {
        var moduleName = _ctx.GetModuleNameForEntity(entity);
        var qualifiedName = NamingConvention.GetQualifiedTableName(entity, moduleName);

        // Quote each part (schema.table → "schema"."table")
        var dotIndex = qualifiedName.IndexOf('.');
        if (dotIndex >= 0)
        {
            var schema = qualifiedName[..dotIndex];
            var table = qualifiedName[(dotIndex + 1)..];
            return $"{NamingConvention.QuoteIdentifier(schema)}.{NamingConvention.QuoteIdentifier(table)}";
        }

        return NamingConvention.QuoteIdentifier(qualifiedName);
    }

    /// <summary>
    /// Get primary key column names for the ON CONFLICT clause.
    /// Returns the seed column names that correspond to entity PK fields.
    /// </summary>
    private static List<string> GetPrimaryKeyColumns(BmSeedDef seed, BmEntity entity)
    {
        var pkFields = entity.Fields.Where(f => f.IsKey).Select(f => f.Name).ToList();

        // Filter to only PK columns that appear in the seed's column list
        var pkColumns = new List<string>();
        foreach (var col in seed.Columns)
        {
            if (pkFields.Any(pk => pk.Equals(col, StringComparison.OrdinalIgnoreCase)))
            {
                pkColumns.Add(col);
            }
        }

        return pkColumns;
    }

    /// <summary>
    /// Escape a SQL string literal with single quotes.
    /// </summary>
    private static string EscapeSqlString(string value)
    {
        var escaped = value.Replace("'", "''");
        return $"'{escaped}'";
    }

    /// <summary>
    /// Format a decimal value using invariant culture.
    /// </summary>
    private static string FormatDecimal(object? value)
    {
        if (value == null) return "0";
        return Convert.ToDecimal(value).ToString(CultureInfo.InvariantCulture);
    }
}
