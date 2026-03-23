namespace BMMDL.Runtime.DataAccess;

using BMMDL.MetaModel.Structure;
using BMMDL.MetaModel.Types;
using BMMDL.MetaModel.Utilities;
using Npgsql;
using System.Text;

/// <summary>
/// Handles OData $search expression parsing and SQL generation
/// for full-text search across string/localized fields.
/// </summary>
internal class SearchQueryBuilder
{
    // ── Types ────────────────────────────────────────────────────────

    internal enum SearchOperator { And, Or }

    internal class SearchTerm
    {
        public string Value { get; set; } = "";
        public SearchOperator Operator { get; set; } = SearchOperator.And;
        public bool IsNegated { get; set; }
    }

    // ── Public helpers (static) ──────────────────────────────────────

    /// <summary>
    /// Return column names of all string/localized fields suitable for $search.
    /// </summary>
    internal static List<string> GetSearchableFields(BmEntity entity)
    {
        return entity.Fields
            .Where(f => f.TypeRef is BmPrimitiveType pt && pt.Kind == BmPrimitiveKind.String
                     || f.TypeRef is BmLocalizedType
                     || (f.TypeRef == null && f.TypeString != null && f.TypeString.StartsWith("String", StringComparison.OrdinalIgnoreCase)))
            .Select(f => NamingConvention.GetColumnName(f.Name))
            .ToList();
    }

    /// <summary>
    /// Parse an OData $search expression into structured terms with operators (AND/OR/NOT).
    /// </summary>
    private const int MaxSearchTerms = 100;

    internal static List<SearchTerm> ParseSearchExpression(string search)
    {
        var terms = new List<SearchTerm>();
        var i = 0;
        SearchOperator pendingOp = SearchOperator.And;

        while (i < search.Length)
        {
            if (terms.Count >= MaxSearchTerms)
                break; // Limit search complexity
            // Skip whitespace
            while (i < search.Length && char.IsWhiteSpace(search[i]))
                i++;
            if (i >= search.Length)
                break;

            // Check for NOT
            if (i + 3 < search.Length &&
                search.Substring(i, 3).Equals("NOT", StringComparison.OrdinalIgnoreCase) &&
                (i + 3 >= search.Length || !char.IsLetterOrDigit(search[i + 3])))
            {
                i += 3;
                // Skip whitespace after NOT
                while (i < search.Length && char.IsWhiteSpace(search[i]))
                    i++;

                var innerTerm = ReadNextToken(search, ref i);
                if (!string.IsNullOrEmpty(innerTerm))
                {
                    terms.Add(new SearchTerm { Value = innerTerm, Operator = pendingOp, IsNegated = true });
                    pendingOp = SearchOperator.And;
                }
                continue;
            }

            // Check for OR
            if (i + 2 < search.Length &&
                search.Substring(i, 2).Equals("OR", StringComparison.OrdinalIgnoreCase) &&
                (i + 2 >= search.Length || !char.IsLetterOrDigit(search[i + 2])))
            {
                pendingOp = SearchOperator.Or;
                i += 2;
                continue;
            }

            // Check for AND (explicit)
            if (i + 3 < search.Length &&
                search.Substring(i, 3).Equals("AND", StringComparison.OrdinalIgnoreCase) &&
                (i + 3 >= search.Length || !char.IsLetterOrDigit(search[i + 3])))
            {
                pendingOp = SearchOperator.And;
                i += 3;
                continue;
            }

            // Read token (quoted phrase or single word)
            var token = ReadNextToken(search, ref i);
            if (!string.IsNullOrEmpty(token))
            {
                terms.Add(new SearchTerm { Value = token, Operator = pendingOp });
                pendingOp = SearchOperator.And; // Reset to AND (implicit)
            }
        }

        return terms;
    }

    // ── Instance method (needs parameter list) ──────────────────────

    /// <summary>
    /// Append a $search WHERE clause to the given list if search is non-empty.
    /// </summary>
    internal void AddSearchFilter(
        List<string> whereClauses,
        BmEntity entity,
        string? search,
        List<NpgsqlParameter> parameters,
        bool caseSensitive = false)
    {
        if (string.IsNullOrWhiteSpace(search))
            return;

        var searchFields = GetSearchableFields(entity);
        if (searchFields.Count == 0)
            return;

        var likeOperator = caseSensitive ? "LIKE" : "ILIKE";

        // Parse search expression into terms with operators
        var terms = ParseSearchExpression(search.Trim());
        if (terms.Count == 0)
            return;

        // Build SQL for each term and combine
        var sqlCondition = BuildSearchTermsSql(terms, searchFields, likeOperator, parameters);
        if (!string.IsNullOrEmpty(sqlCondition))
            whereClauses.Add(sqlCondition);
    }

    // ── Private helpers ─────────────────────────────────────────────

    private static string ReadNextToken(string search, ref int i)
    {
        if (i >= search.Length)
            return "";

        // Quoted phrase
        if (search[i] == '"')
        {
            i++; // skip opening quote
            var start = i;
            while (i < search.Length && search[i] != '"')
                i++;
            var token = search.Substring(start, i - start);
            if (i < search.Length)
                i++; // skip closing quote
            return token;
        }

        // Unquoted word
        {
            var start = i;
            while (i < search.Length && !char.IsWhiteSpace(search[i]) && search[i] != '"')
                i++;
            return search.Substring(start, i - start);
        }
    }

    /// <summary>
    /// Escape LIKE/ILIKE wildcard characters in user-supplied search values.
    /// </summary>
    private static string EscapeLikeValue(string value)
    {
        return value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }

    private static string BuildSearchTermsSql(
        List<SearchTerm> terms,
        List<string> searchFields,
        string likeOperator,
        List<NpgsqlParameter> parameters)
    {
        if (terms.Count == 0)
            return "";

        var parts = new List<string>();

        foreach (var term in terms)
        {
            var paramName = $"@p{parameters.Count}";
            var escapedValue = EscapeLikeValue(term.Value);
            parameters.Add(new NpgsqlParameter(paramName, $"%{escapedValue}%"));

            var fieldConditions = searchFields
                .Select(f => $"COALESCE({f}::text, '') {likeOperator} {paramName}")
                .ToList();

            var termSql = $"({string.Join(" OR ", fieldConditions)})";

            if (term.IsNegated)
                termSql = $"NOT {termSql}";

            if (parts.Count > 0)
            {
                var combiner = term.Operator == SearchOperator.Or ? " OR " : " AND ";
                parts.Add(combiner);
            }
            parts.Add(termSql);
        }

        return $"({string.Concat(parts)})";
    }
}
