namespace BMMDL.Runtime.DataAccess;

using System.Text.RegularExpressions;

/// <summary>
/// Options for an expanded navigation property.
/// Supports nested OData query options within $expand.
/// </summary>
public record ExpandOptions
{
    /// <summary>
    /// Property/field selection for the expanded entity.
    /// </summary>
    public string? Select { get; init; }
    
    /// <summary>
    /// Filter expression for the expanded entity.
    /// </summary>
    public string? Filter { get; init; }
    
    /// <summary>
    /// Order by clause for the expanded entity (1:N only).
    /// </summary>
    public string? OrderBy { get; init; }
    
    /// <summary>
    /// Maximum items to return (1:N only).
    /// </summary>
    public int? Top { get; init; }
    
    /// <summary>
    /// Items to skip (1:N only).
    /// </summary>
    public int? Skip { get; init; }
    
    /// <summary>
    /// OData v4 $levels for recursive expansion.
    /// -1 means "max" (unlimited until recursion ends).
    /// Positive values indicate the specific number of levels.
    /// </summary>
    public int? Levels { get; init; }
}

/// <summary>
/// Parses OData $expand expressions into structured options.
/// Supports nested query options for each navigation property.
/// </summary>
/// <remarks>
/// Supported syntax:
/// - Simple: $expand=customer
/// - Multiple: $expand=customer,items
/// - Nested: $expand=customer($select=name,email)
/// - Full: $expand=customer($select=name;$filter=active eq true;$top=5)
/// </remarks>
public class ExpandExpressionParser
{
    /// <summary>
    /// Parse an $expand expression into navigation properties with options.
    /// </summary>
    /// <param name="expand">OData $expand value.</param>
    /// <returns>Dictionary of navigation name -> options.</returns>
    /// <example>
    /// Input: "customer,items($top=5)"
    /// Output: { "customer": {}, "items": { Top = 5 } }
    /// </example>
    public Dictionary<string, ExpandOptions> Parse(string expand)
    {
        var result = new Dictionary<string, ExpandOptions>(StringComparer.OrdinalIgnoreCase);
        
        if (string.IsNullOrWhiteSpace(expand))
            return result;
        
        // Split by comma, but respect parentheses
        var parts = SplitByComma(expand.Trim());
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            
            // Check for nested options: navName($select=x;$top=5)
            var parenIndex = trimmed.IndexOf('(');
            if (parenIndex > 0 && trimmed.EndsWith(')'))
            {
                var navName = trimmed.Substring(0, parenIndex).Trim();
                var optionsStr = trimmed.Substring(parenIndex + 1, trimmed.Length - parenIndex - 2);
                result[navName] = ParseNestedOptions(optionsStr);
            }
            else
            {
                // Simple navigation property
                result[trimmed] = new ExpandOptions();
            }
        }
        
        return result;
    }

    /// <summary>
    /// Split by comma while respecting parentheses depth.
    /// </summary>
    private static List<string> SplitByComma(string input)
    {
        var results = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();
        
        foreach (var c in input)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                results.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        
        if (current.Length > 0)
            results.Add(current.ToString());
        
        return results;
    }

    /// <summary>
    /// Parse nested query options: $select=a,b;$filter=x eq 1;$top=10;$levels=3
    /// Delimiter is semicolon (;) within parentheses.
    /// </summary>
    private static ExpandOptions ParseNestedOptions(string optionsStr)
    {
        var options = new ExpandOptions();
        
        if (string.IsNullOrWhiteSpace(optionsStr))
            return options;
        
        // Split by semicolon for multiple options
        var parts = optionsStr.Split(';');
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            
            // Check for known options: $select, $filter, $orderby, $top, $skip, $levels
            var match = Regex.Match(trimmed, @"^\$(\w+)=(.+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;
            
            var optionName = match.Groups[1].Value.ToLowerInvariant();
            var optionValue = match.Groups[2].Value.Trim();
            
            options = optionName switch
            {
                "select" => options with { Select = optionValue },
                "filter" => options with { Filter = optionValue },
                "orderby" => options with { OrderBy = optionValue },
                "top" when int.TryParse(optionValue, out var top) => options with { Top = top },
                "skip" when int.TryParse(optionValue, out var skip) => options with { Skip = skip },
                // OData v4 $levels: supports "max" or numeric value
                "levels" => ParseLevelsOption(optionValue, options),
                _ => options // Ignore unknown options
            };
        }
        
        return options;
    }

    /// <summary>
    /// Parse $levels option value. "max" returns -1, otherwise parse as integer.
    /// </summary>
    private static ExpandOptions ParseLevelsOption(string value, ExpandOptions options)
    {
        if (value.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return options with { Levels = -1 };
        }
        
        if (int.TryParse(value, out var levels) && levels > 0)
        {
            return options with { Levels = levels };
        }
        
        return options; // Invalid $levels value, ignore
    }
}
