using System;
using System.Collections.Generic;

namespace BMMDL.CodeGen.Visitors;

/// <summary>
/// Registry for mapping BMMDL function names to PostgreSQL function implementations.
/// Supports parameter transformations and custom SQL generation.
/// </summary>
public class FunctionMappingRegistry
{
    private readonly Dictionary<string, FunctionMapping> _mappings = new();

    public FunctionMappingRegistry()
    {
        InitializeStandardMappings();
    }

    /// <summary>
    /// Get the PostgreSQL function implementation for a BMMDL function.
    /// </summary>
    public FunctionMapping? GetMapping(string functionName)
    {
        _mappings.TryGetValue(functionName.ToUpperInvariant(), out var mapping);
        return mapping;
    }

    private void InitializeStandardMappings()
    {
        // String Functions
        RegisterSimple("UPPER", "UPPER");
        RegisterSimple("LOWER", "LOWER");
        RegisterSimple("TRIM", "TRIM");
        RegisterSimple("LTRIM", "LTRIM");
        RegisterSimple("RTRIM", "RTRIM");
        RegisterSimple("LENGTH", "LENGTH");
        
        // SUBSTRING(str, start, length) -> SUBSTRING(str FROM start FOR length)
        Register("SUBSTRING", args =>
        {
            if (args.Length == 2)
                return $"SUBSTRING({args[0]} FROM {args[1]})";
            if (args.Length == 3)
                return $"SUBSTRING({args[0]} FROM {args[1]} FOR {args[2]})";
            throw new ArgumentException("SUBSTRING requires 2 or 3 arguments");
        });
        
        // CONCAT(a, b, c, ...) -> a || b || c || ...
        Register("CONCAT", args =>
        {
            if (args.Length == 0)
                throw new ArgumentException("CONCAT requires at least 1 argument");
            return string.Join(" || ", args);
        });
        
        // String replacement
        RegisterSimple("REPLACE", "REPLACE");
        
        // Numeric Functions
        RegisterSimple("ABS", "ABS");
        RegisterSimple("CEIL", "CEIL");
        RegisterSimple("FLOOR", "FLOOR");
        RegisterSimple("ROUND", "ROUND");
        RegisterSimple("SQRT", "SQRT");
        RegisterSimple("POWER", "POWER");
        RegisterSimple("MOD", "MOD");
        
        // Date/Time Functions
        RegisterSimple("NOW", "NOW");
        RegisterSimple("CURRENT_DATE", "CURRENT_DATE");
        RegisterSimple("CURRENT_TIME", "CURRENT_TIME");
        RegisterSimple("CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP");
        
        // DATEADD(interval, amount, date) -> date + (amount * INTERVAL '1 interval')
        Register("DATEADD", args =>
        {
            if (args.Length != 3)
                throw new ArgumentException("DATEADD requires 3 arguments: interval, amount, date");
            var interval = args[0].Trim('\'').ToUpperInvariant();
            var allowedIntervals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "YEAR", "YEARS", "MONTH", "MONTHS", "DAY", "DAYS", "HOUR", "HOURS", "MINUTE", "MINUTES", "SECOND", "SECONDS" };
            if (!allowedIntervals.Contains(interval))
                throw new ArgumentException($"DATEADD: invalid interval '{interval}'. Allowed: {string.Join(", ", allowedIntervals)}");
            return $"({args[2]} + ({args[1]}) * INTERVAL '1 {interval}')";
        });
        
        // DATEDIFF(part, start, end) -> part-aware date difference
        Register("DATEDIFF", args =>
        {
            if (args.Length != 3)
                throw new ArgumentException("DATEDIFF requires 3 arguments: part, startDate, endDate");
            var part = args[0].Trim('\'').ToUpperInvariant();
            return part switch
            {
                "YEAR" or "YEARS" => $"(EXTRACT(YEAR FROM {args[2]}) - EXTRACT(YEAR FROM {args[1]}))",
                "MONTH" or "MONTHS" => $"((EXTRACT(YEAR FROM {args[2]}) - EXTRACT(YEAR FROM {args[1]})) * 12 + EXTRACT(MONTH FROM {args[2]}) - EXTRACT(MONTH FROM {args[1]}))",
                "HOUR" or "HOURS" => $"EXTRACT(EPOCH FROM ({args[2]}::timestamp - {args[1]}::timestamp)) / 3600",
                "MINUTE" or "MINUTES" => $"EXTRACT(EPOCH FROM ({args[2]}::timestamp - {args[1]}::timestamp)) / 60",
                "SECOND" or "SECONDS" => $"EXTRACT(EPOCH FROM ({args[2]}::timestamp - {args[1]}::timestamp))",
                _ => $"({args[2]}::date - {args[1]}::date)" // DAY/DAYS or default
            };
        });
        
        // EXTRACT(part FROM date) -> EXTRACT(part FROM date)
        Register("EXTRACT", args =>
        {
            if (args.Length != 2)
                throw new ArgumentException("EXTRACT requires 2 arguments: part, date");
            return $"EXTRACT({args[0]} FROM {args[1]})";
        });
        
        // Aggregate Functions (pass-through to SQL)
        RegisterSimple("COUNT", "COUNT");
        RegisterSimple("SUM", "SUM");
        RegisterSimple("AVG", "AVG");
        RegisterSimple("MIN", "MIN");
        RegisterSimple("MAX", "MAX");
        
        // Conditional Functions
        RegisterSimple("COALESCE", "COALESCE");
        RegisterSimple("NULLIF", "NULLIF");
        
        // GREATEST/LEAST
        RegisterSimple("GREATEST", "GREATEST");
        RegisterSimple("LEAST", "LEAST");
        
        // Type Conversion
        RegisterSimple("CAST", "CAST");
        
        // Logical
        Register("IIF", args =>
        {
            if (args.Length != 3)
                throw new ArgumentException("IIF requires exactly 3 arguments: condition, trueValue, falseValue");
            return $"CASE WHEN {args[0]} THEN {args[1]} ELSE {args[2]} END";
        });

        // String Functions (additional)
        // INSTR(string, substring) -> strpos(string, substring)
        RegisterSimple("INSTR", "strpos");
        RegisterSimple("LPAD", "LPAD");
        RegisterSimple("RPAD", "RPAD");

        // Conditional
        RegisterSimple("IFNULL", "COALESCE");

        // DECODE(expr, search1, result1, ..., default) -> CASE expr WHEN search1 THEN result1 ... ELSE default END
        Register("DECODE", args =>
        {
            if (args.Length < 3)
                throw new ArgumentException("DECODE requires at least 3 arguments: expr, search, result");
            var sb = new System.Text.StringBuilder($"CASE {args[0]}");
            for (int i = 1; i + 1 < args.Length; i += 2)
            {
                sb.Append($" WHEN {args[i]} THEN {args[i + 1]}");
            }
            // If odd remaining arg count, it's the default
            if (args.Length % 2 == 0)
            {
                sb.Append($" ELSE {args[^1]}");
            }
            sb.Append(" END");
            return sb.ToString();
        });

        // Type Conversion Functions
        Register("TO_INTEGER", args =>
        {
            if (args.Length != 1) throw new ArgumentException("TO_INTEGER requires exactly 1 argument");
            return $"({args[0]})::integer";
        });
        Register("TO_DECIMAL", args =>
        {
            if (args.Length != 1) throw new ArgumentException("TO_DECIMAL requires exactly 1 argument");
            return $"({args[0]})::numeric";
        });
        Register("TO_DATE", args =>
        {
            if (args.Length != 1) throw new ArgumentException("TO_DATE requires exactly 1 argument");
            return $"({args[0]})::date";
        });
        Register("TO_STRING", args =>
        {
            if (args.Length != 1) throw new ArgumentException("TO_STRING requires exactly 1 argument");
            return $"({args[0]})::text";
        });
        Register("TO_TIME", args =>
        {
            if (args.Length != 1) throw new ArgumentException("TO_TIME requires exactly 1 argument");
            return $"({args[0]})::time";
        });
        Register("TO_TIMESTAMP", args =>
        {
            if (args.Length != 1) throw new ArgumentException("TO_TIMESTAMP requires exactly 1 argument");
            return $"({args[0]})::timestamp";
        });

        // Statistical Aggregate Functions
        RegisterSimple("STDDEV", "stddev");
        RegisterSimple("VARIANCE", "variance");

        // Date-Part Extraction Functions — YEAR(date) -> EXTRACT(YEAR FROM date)
        Register("YEAR", args => { if (args.Length != 1) throw new ArgumentException("YEAR requires exactly 1 argument"); return $"EXTRACT(YEAR FROM {args[0]})"; });
        Register("MONTH", args => { if (args.Length != 1) throw new ArgumentException("MONTH requires exactly 1 argument"); return $"EXTRACT(MONTH FROM {args[0]})"; });
        Register("DAY", args => { if (args.Length != 1) throw new ArgumentException("DAY requires exactly 1 argument"); return $"EXTRACT(DAY FROM {args[0]})"; });
        Register("HOUR", args => { if (args.Length != 1) throw new ArgumentException("HOUR requires exactly 1 argument"); return $"EXTRACT(HOUR FROM {args[0]})"; });
        Register("MINUTE", args => { if (args.Length != 1) throw new ArgumentException("MINUTE requires exactly 1 argument"); return $"EXTRACT(MINUTE FROM {args[0]})"; });
        Register("SECOND", args => { if (args.Length != 1) throw new ArgumentException("SECOND requires exactly 1 argument"); return $"EXTRACT(SECOND FROM {args[0]})"; });
        Register("DAYOFWEEK", args => { if (args.Length != 1) throw new ArgumentException("DAYOFWEEK requires exactly 1 argument"); return $"EXTRACT(DOW FROM {args[0]})"; });
        Register("WEEKOFYEAR", args => { if (args.Length != 1) throw new ArgumentException("WEEKOFYEAR requires exactly 1 argument"); return $"EXTRACT(WEEK FROM {args[0]})"; });

        // Date Arithmetic Functions
        Register("ADD_DAYS", args => { if (args.Length != 2) throw new ArgumentException("ADD_DAYS requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 day')"; });
        Register("ADD_MONTHS", args => { if (args.Length != 2) throw new ArgumentException("ADD_MONTHS requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 month')"; });
        Register("ADD_YEARS", args => { if (args.Length != 2) throw new ArgumentException("ADD_YEARS requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 year')"; });

        // Format function — FORMAT(value, format) -> to_char(value, format)
        Register("FORMAT", args => args.Length >= 2 ? $"to_char({args[0]}, {args[1]})" : $"({args[0]})::text");

        // Fiscal Functions — stubs mapped to calendar equivalents
        Register("FISCAL_YEAR", args => $"EXTRACT(YEAR FROM {args[0]})");
        Register("FISCAL_PERIOD", args => $"EXTRACT(MONTH FROM {args[0]})");

        // Pad Functions (camelCase aliases → PostgreSQL)
        Register("PAD_LEFT", args => { if (args.Length < 2) throw new ArgumentException("PAD_LEFT requires at least 2 arguments"); return $"LPAD({string.Join(", ", args)})"; });
        Register("PAD_RIGHT", args => { if (args.Length < 2) throw new ArgumentException("PAD_RIGHT requires at least 2 arguments"); return $"RPAD({string.Join(", ", args)})"; });

        // Sequence Functions — generate PostgreSQL nextval/currval/setval
        Register("NEXT_SEQUENCE", args => { if (args.Length != 1) throw new ArgumentException("NEXT_SEQUENCE requires exactly 1 argument"); return $"nextval({args[0]})"; });
        Register("CURRENT_SEQUENCE", args => { if (args.Length != 1) throw new ArgumentException("CURRENT_SEQUENCE requires exactly 1 argument"); return $"currval({args[0]})"; });
        Register("FORMAT_SEQUENCE", args => { if (args.Length < 1) throw new ArgumentException("FORMAT_SEQUENCE requires at least 1 argument"); return args.Length >= 2 ? $"lpad(currval({args[0]})::text, {args[1]}, '0')" : $"currval({args[0]})::text"; });
        Register("RESET_SEQUENCE", args => { if (args.Length != 1) throw new ArgumentException("RESET_SEQUENCE requires exactly 1 argument"); return $"setval({args[0]}, 1, false)"; });
        Register("SET_SEQUENCE", args => { if (args.Length != 2) throw new ArgumentException("SET_SEQUENCE requires exactly 2 arguments"); return $"setval({args[0]}, {args[1]})"; });

        // Domain-Specific Functions (stubs - pass through first arg)
        Register("CURRENCY_CONVERSION", args =>
        {
            // Stub: just return the amount expression (first arg)
            return args.Length > 0 ? args[0] : "NULL";
        });
        Register("UNIT_CONVERSION", args =>
        {
            // Stub: just return the value expression (first arg)
            return args.Length > 0 ? args[0] : "NULL";
        });

        // ==================== Missing Numeric Functions ====================

        // TRUNC(value) -> TRUNC(value)
        RegisterSimple("TRUNC", "TRUNC");

        // SIGN(value) -> SIGN(value)
        RegisterSimple("SIGN", "SIGN");

        // CEILING as alias for CEIL
        RegisterSimple("CEILING", "CEIL");

        // ==================== Missing String Functions ====================

        // LEN as alias for LENGTH
        RegisterSimple("LEN", "LENGTH");

        // LEFT(str, n) -> LEFT(str, n)
        RegisterSimple("LEFT", "LEFT");

        // RIGHT(str, n) -> RIGHT(str, n)
        RegisterSimple("RIGHT", "RIGHT");

        // CONTAINS(str, search) -> (strpos(str, search) > 0)
        Register("CONTAINS", args =>
        {
            if (args.Length != 2)
                throw new ArgumentException("CONTAINS requires 2 arguments: string, search");
            return $"(strpos({args[0]}, {args[1]}) > 0)";
        });

        // STARTSWITH(str, prefix) -> (str LIKE prefix || '%')
        Register("STARTSWITH", args =>
        {
            if (args.Length != 2)
                throw new ArgumentException("STARTSWITH requires 2 arguments: string, prefix");
            return $"({args[0]} LIKE {args[1]} || '%')";
        });

        // ENDSWITH(str, suffix) -> (str LIKE '%' || suffix)
        Register("ENDSWITH", args =>
        {
            if (args.Length != 2)
                throw new ArgumentException("ENDSWITH requires 2 arguments: string, suffix");
            return $"({args[0]} LIKE '%' || {args[1]})";
        });

        // INDEXOF(str, search) -> (strpos(str, search) - 1) (0-based)
        Register("INDEXOF", args =>
        {
            if (args.Length != 2)
                throw new ArgumentException("INDEXOF requires 2 arguments: string, search");
            return $"(strpos({args[0]}, {args[1]}) - 1)";
        });

        // camelCase pad aliases -> PostgreSQL LPAD/RPAD
        Register("PADLEFT", args => { if (args.Length < 2) throw new ArgumentException("PADLEFT requires at least 2 arguments"); return $"LPAD({string.Join(", ", args)})"; });
        Register("PADRIGHT", args => { if (args.Length < 2) throw new ArgumentException("PADRIGHT requires at least 2 arguments"); return $"RPAD({string.Join(", ", args)})"; });

        // ==================== Missing Date/Time Functions ====================

        // TODAY() -> CURRENT_DATE
        Register("TODAY", _ => "CURRENT_DATE");

        // UTCNOW() -> (NOW() AT TIME ZONE 'UTC')
        Register("UTCNOW", _ => "(NOW() AT TIME ZONE 'UTC')");

        // DAYOFYEAR(date) -> EXTRACT(DOY FROM date)
        Register("DAYOFYEAR", args => { if (args.Length != 1) throw new ArgumentException("DAYOFYEAR requires exactly 1 argument"); return $"EXTRACT(DOY FROM {args[0]})"; });

        // camelCase date arithmetic aliases
        Register("ADDDAYS", args => { if (args.Length != 2) throw new ArgumentException("ADDDAYS requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 day')"; });
        Register("ADDMONTHS", args => { if (args.Length != 2) throw new ArgumentException("ADDMONTHS requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 month')"; });
        Register("ADDYEARS", args => { if (args.Length != 2) throw new ArgumentException("ADDYEARS requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 year')"; });
        Register("ADDHOURS", args => { if (args.Length != 2) throw new ArgumentException("ADDHOURS requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 hour')"; });
        Register("ADDMINUTES", args => { if (args.Length != 2) throw new ArgumentException("ADDMINUTES requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 minute')"; });

        // Underscore forms for hours/minutes
        Register("ADD_HOURS", args => { if (args.Length != 2) throw new ArgumentException("ADD_HOURS requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 hour')"; });
        Register("ADD_MINUTES", args => { if (args.Length != 2) throw new ArgumentException("ADD_MINUTES requires exactly 2 arguments"); return $"({args[0]} + ({args[1]}) * INTERVAL '1 minute')"; });

        // ==================== Missing GUID Functions ====================

        // NEWGUID() -> gen_random_uuid()
        Register("NEWGUID", _ => "gen_random_uuid()");

        // NEWID as alias for NEWGUID
        Register("NEWID", _ => "gen_random_uuid()");

        // TOGUID(str) -> (str)::uuid
        Register("TOGUID", args =>
        {
            if (args.Length != 1) throw new ArgumentException("TOGUID requires exactly 1 argument");
            return $"({args[0]})::uuid";
        });

        // ==================== Missing Boolean/Null Functions ====================

        // ISNULL(expr) -> (expr IS NULL)
        Register("ISNULL", args =>
        {
            if (args.Length != 1) throw new ArgumentException("ISNULL requires exactly 1 argument");
            return $"({args[0]} IS NULL)";
        });

        // ISNOTNULL(expr) -> (expr IS NOT NULL)
        Register("ISNOTNULL", args =>
        {
            if (args.Length != 1) throw new ArgumentException("ISNOTNULL requires exactly 1 argument");
            return $"({args[0]} IS NOT NULL)";
        });
    }

    private void RegisterSimple(string bmmdlName, string pgName)
    {
        _mappings[bmmdlName.ToUpperInvariant()] = new FunctionMapping(
            bmmdlName,
            pgName,
            args => $"{pgName}({string.Join(", ", args)})"
        );
    }

    private void Register(string bmmdlName, Func<string[], string> translator)
    {
        _mappings[bmmdlName.ToUpperInvariant()] = new FunctionMapping(
            bmmdlName,
            bmmdlName, // PostgreSQL name (may not be used)
            translator
        );
    }
}

/// <summary>
/// Represents a mapping from BMMDL function to PostgreSQL implementation.
/// </summary>
public class FunctionMapping
{
    public string BmmdlName { get; }
    public string PostgresName { get; }
    public Func<string[], string> Translator { get; }

    public FunctionMapping(string bmmdlName, string postgresName, Func<string[], string> translator)
    {
        BmmdlName = bmmdlName;
        PostgresName = postgresName;
        Translator = translator;
    }

    /// <summary>
    /// Translate the function call with given SQL argument strings.
    /// </summary>
    public string Translate(string[] args) => Translator(args);
}
