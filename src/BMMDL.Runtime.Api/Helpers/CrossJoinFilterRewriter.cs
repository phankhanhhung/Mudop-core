namespace BMMDL.Runtime.Api.Helpers;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Utilities;
using BMMDL.Runtime.DataAccess;
using Npgsql;
using System.Text.RegularExpressions;

/// <summary>
/// Rewrites $filter expressions for cross-join queries by replacing EntitySet/Field references
/// with table aliases and parameterizing values via FilterExpressionParser.
/// </summary>
public static class CrossJoinFilterRewriter
{
    /// <summary>
    /// Rewrite a $filter expression for cross-join by:
    /// 1. Parsing through FilterExpressionParser for OData→SQL conversion and value parameterization
    /// 2. Replacing concatenated navigation path columns with qualified alias.column references
    /// </summary>
    public static (string WhereClause, List<NpgsqlParameter> Parameters)? Rewrite(
        string filter,
        List<(string name, BmEntity def)> entities,
        List<string> tableAliases)
    {
        // Step 1: Parse the OData filter into parameterized SQL
        // FilterExpressionParser handles: eq→=, ne→!=, gt→>, etc. and parameterizes values
        // Navigation paths like "Customer/Name" become "customer_name" via ResolveNavigationPath
        var parser = new FilterExpressionParser();
        var (sql, parameters) = parser.Parse(filter);

        // Step 2: Replace concatenated navigation path columns with qualified alias.column
        // e.g., "customer_name" → t0."name"
        for (int i = 0; i < entities.Count; i++)
        {
            var entityPrefix = NamingConvention.ToSnakeCase(entities[i].name);
            foreach (var field in entities[i].def.Fields)
            {
                var colName = NamingConvention.ToSnakeCase(field.Name);
                var navPathCol = $"{entityPrefix}_{colName}";
                var qualifiedCol = $"{tableAliases[i]}.{NamingConvention.QuoteIdentifier(colName)}";
                // Use word boundary replacement to avoid partial matches
                sql = Regex.Replace(sql, $@"\b{Regex.Escape(navPathCol)}\b", qualifiedCol);
            }
        }

        return (sql, parameters.ToList());
    }
}
