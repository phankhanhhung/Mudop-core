namespace BMMDL.Runtime.DataAccess;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Parses OData $search expressions into PostgreSQL full-text search queries.
/// Converts search terms to tsquery format for use with tsvector.
/// </summary>
/// <remarks>
/// Supported syntax:
/// - Simple terms: "blue" → blue:*
/// - Multiple terms (implicit AND): "blue car" → blue:* &amp; car:*
/// - OR operator: "blue OR red" → blue:* | red:*
/// - AND operator: "blue AND red" → blue:* &amp; red:*
/// - NOT operator: "NOT green" → !green:*
/// - Phrase search: "\"exact phrase\"" → 'exact phrase'
/// </remarks>
public class SearchExpressionParser
{
    /// <summary>
    /// Parse OData $search expression to PostgreSQL tsquery.
    /// </summary>
    /// <param name="search">OData $search value.</param>
    /// <param name="searchFields">Fields to include in tsvector (snake_case).</param>
    /// <returns>SQL WHERE clause fragment for full-text search.</returns>
    /// <example>
    /// Input: "blue car", ["name", "description"]
    /// Output: to_tsvector('english', COALESCE(name,'') || ' ' || COALESCE(description,'')) @@ plainto_tsquery('english', @search_param)
    /// </example>
    public (string SqlClause, string TsQueryValue) Parse(string search, IEnumerable<string> searchFields)
    {
        if (string.IsNullOrWhiteSpace(search))
            return ("", "");

        var fields = searchFields.ToList();
        if (fields.Count == 0)
            return ("", "");

        // Build tsvector expression from fields
        var tsvectorExpr = BuildTsvectorExpression(fields);
        
        // Convert search to tsquery format
        var tsqueryValue = ConvertToTsquery(search.Trim());
        
        // SECURITY FIX: Use parameterized query pattern
        // The caller should use the tsqueryValue as a parameter, not embed it in SQL
        // Return SQL clause with placeholder that caller should parameterize
        var sqlClause = $"{tsvectorExpr} @@ to_tsquery('english', @search_query)";
        
        return (sqlClause, tsqueryValue);
    }

    /// <summary>
    /// Build tsvector expression from multiple fields.
    /// </summary>
    private static string BuildTsvectorExpression(List<string> fields)
    {
        if (fields.Count == 1)
            return $"to_tsvector('english', COALESCE({fields[0]}, ''))";

        // Concatenate multiple fields
        var concatParts = fields.Select(f => $"COALESCE({f}, '')");
        var concatenated = string.Join(" || ' ' || ", concatParts);
        return $"to_tsvector('english', {concatenated})";
    }

    /// <summary>
    /// Convert OData search expression to PostgreSQL tsquery format.
    /// </summary>
    /// <example>
    /// "blue car" → "blue:* &amp; car:*"
    /// "blue OR red" → "blue:* | red:*"
    /// "blue AND NOT green" → "blue:* &amp; !green:*"
    /// "\"exact phrase\"" → "'exact phrase'"
    /// </example>
    private static string ConvertToTsquery(string search)
    {
        var result = new StringBuilder();
        var tokens = Tokenize(search);
        
        string? pendingOperator = null;
        
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.Operator)
            {
                pendingOperator = token.Value.ToUpperInvariant();
                continue;
            }
            
            if (token.Type == TokenType.Phrase || token.Type == TokenType.Word)
            {
                // Add operator before term (except for first term)
                if (result.Length > 0)
                {
                    var op = pendingOperator ?? "AND";
                    result.Append(op switch
                    {
                        "OR" => " | ",
                        "AND" => " & ",
                        "NOT" => " & !",
                        _ => " & "
                    });
                }
                else if (pendingOperator == "NOT")
                {
                    result.Append('!');
                }
                
                // Add term with prefix matching
                if (token.Type == TokenType.Phrase)
                {
                    // Phrase search - use phraseto_tsquery format
                    result.Append($"'{token.Value}'");
                }
                else
                {
                    // Word with prefix matching
                    result.Append($"{SanitizeWord(token.Value)}:*");
                }
                
                pendingOperator = null;
            }
        }
        
        return result.ToString();
    }

    /// <summary>
    /// Tokenize search string into words, phrases, and operators.
    /// </summary>
    private static List<Token> Tokenize(string search)
    {
        var tokens = new List<Token>();
        var i = 0;
        
        while (i < search.Length)
        {
            // Skip whitespace
            while (i < search.Length && char.IsWhiteSpace(search[i]))
                i++;
            
            if (i >= search.Length)
                break;
            
            // Check for quoted phrase
            if (search[i] == '"')
            {
                var end = search.IndexOf('"', i + 1);
                if (end > i)
                {
                    tokens.Add(new Token(TokenType.Phrase, search.Substring(i + 1, end - i - 1)));
                    i = end + 1;
                    continue;
                }
            }
            
            // Read word/operator
            var wordStart = i;
            while (i < search.Length && !char.IsWhiteSpace(search[i]) && search[i] != '"')
                i++;
            
            var word = search.Substring(wordStart, i - wordStart);
            
            // Check if it's an operator
            if (word.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
                word.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
                word.Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token(TokenType.Operator, word));
            }
            else
            {
                tokens.Add(new Token(TokenType.Word, word));
            }
        }
        
        return tokens;
    }

    /// <summary>
    /// Sanitize word for tsquery (remove special characters).
    /// </summary>
    private static string SanitizeWord(string word)
    {
        // Remove any special tsquery characters
        return Regex.Replace(word, @"[^\w]", "", RegexOptions.None);
    }

    /// <summary>
    /// Escape string for SQL (prevent injection).
    /// </summary>
    private static string EscapeForSql(string value)
    {
        return value.Replace("'", "''");
    }

    private enum TokenType { Word, Phrase, Operator }
    private record Token(TokenType Type, string Value);
}
