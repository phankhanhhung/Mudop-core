namespace BMMDL.Runtime.Expressions;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Registry of built-in functions for runtime expression evaluation.
/// </summary>
public class FunctionRegistry
{
    private readonly Dictionary<string, Func<object?[], object?>> _functions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FunctionRegistry> _logger;

    public FunctionRegistry() : this(NullLogger<FunctionRegistry>.Instance) { }

    public FunctionRegistry(ILogger<FunctionRegistry> logger)
    {
        _logger = logger;
        RegisterBuiltInFunctions();
    }

    /// <summary>
    /// Check if a function exists.
    /// </summary>
    public bool HasFunction(string name) => _functions.ContainsKey(name);

    /// <summary>
    /// Invoke a function with arguments.
    /// </summary>
    public object? Invoke(string name, object?[] args)
    {
        if (!_functions.TryGetValue(name, out var func))
            throw new InvalidOperationException($"Unknown function: {name}");
        
        return func(args);
    }

    /// <summary>
    /// Register a custom function.
    /// </summary>
    public void Register(string name, Func<object?[], object?> func)
    {
        _functions[name] = func;
    }

    private static void ValidateArgs(string functionName, object?[] args, int minArgs)
    {
        if (args.Length < minArgs)
            throw new ArgumentException(
                $"Function {functionName} requires at least {minArgs} argument(s), but received {args.Length}.");
    }

    private void RegisterBuiltInFunctions()
    {
        // ==================== STRING FUNCTIONS ====================

        _functions["UPPER"] = args => { ValidateArgs("UPPER", args, 1); return args[0]?.ToString()?.ToUpperInvariant(); };
        _functions["LOWER"] = args => { ValidateArgs("LOWER", args, 1); return args[0]?.ToString()?.ToLowerInvariant(); };
        _functions["TRIM"] = args => { ValidateArgs("TRIM", args, 1); return args[0]?.ToString()?.Trim(); };
        _functions["LTRIM"] = args => { ValidateArgs("LTRIM", args, 1); return args[0]?.ToString()?.TrimStart(); };
        _functions["RTRIM"] = args => { ValidateArgs("RTRIM", args, 1); return args[0]?.ToString()?.TrimEnd(); };

        _functions["LENGTH"] = args => { ValidateArgs("LENGTH", args, 1); return args[0]?.ToString()?.Length ?? 0; };
        _functions["LEN"] = _functions["LENGTH"];

        _functions["SUBSTRING"] = args =>
        {
            ValidateArgs("SUBSTRING", args, 2);
            var str = args[0]?.ToString() ?? "";
            var start = Convert.ToInt32(args[1] ?? 0);
            var length = args.Length > 2 ? Convert.ToInt32(args[2]) : str.Length - start;
            
            if (start < 0) start = 0;
            if (start >= str.Length) return "";
            if (start + length > str.Length) length = str.Length - start;
            
            return str.Substring(start, length);
        };
        
        _functions["CONCAT"] = args =>
        {
            return string.Concat(args.Select(a => a?.ToString() ?? ""));
        };
        
        _functions["REPLACE"] = args =>
        {
            ValidateArgs("REPLACE", args, 3);
            var str = args[0]?.ToString() ?? "";
            var oldValue = args[1]?.ToString() ?? "";
            var newValue = args[2]?.ToString() ?? "";
            return str.Replace(oldValue, newValue);
        };

        _functions["LEFT"] = args =>
        {
            ValidateArgs("LEFT", args, 2);
            var str = args[0]?.ToString() ?? "";
            var length = Convert.ToInt32(args[1] ?? 0);
            if (length < 0) length = 0;
            return str.Length <= length ? str : str[..length];
        };

        _functions["RIGHT"] = args =>
        {
            ValidateArgs("RIGHT", args, 2);
            var str = args[0]?.ToString() ?? "";
            var length = Convert.ToInt32(args[1] ?? 0);
            if (length <= 0) return "";
            return str.Length <= length ? str : str[^length..];
        };

        _functions["PADLEFT"] = args =>
        {
            ValidateArgs("PADLEFT", args, 2);
            var str = args[0]?.ToString() ?? "";
            var totalWidth = Convert.ToInt32(args[1] ?? 0);
            if (totalWidth < 0) totalWidth = 0;
            if (totalWidth > 10000) totalWidth = 10000; // Prevent OOM
            var padCharStr = args.Length > 2 ? (args[2]?.ToString() ?? " ") : " ";
            var padChar = padCharStr.Length > 0 ? padCharStr[0] : ' ';
            return str.PadLeft(totalWidth, padChar);
        };

        _functions["PADRIGHT"] = args =>
        {
            ValidateArgs("PADRIGHT", args, 2);
            var str = args[0]?.ToString() ?? "";
            var totalWidth = Convert.ToInt32(args[1] ?? 0);
            if (totalWidth < 0) totalWidth = 0;
            if (totalWidth > 10000) totalWidth = 10000; // Prevent OOM
            var padCharStr = args.Length > 2 ? (args[2]?.ToString() ?? " ") : " ";
            var padChar = padCharStr.Length > 0 ? padCharStr[0] : ' ';
            return str.PadRight(totalWidth, padChar);
        };

        _functions["CONTAINS"] = args =>
        {
            ValidateArgs("CONTAINS", args, 2);
            var str = args[0]?.ToString() ?? "";
            var search = args[1]?.ToString() ?? "";
            return str.Contains(search, StringComparison.OrdinalIgnoreCase);
        };

        _functions["STARTSWITH"] = args =>
        {
            ValidateArgs("STARTSWITH", args, 2);
            var str = args[0]?.ToString() ?? "";
            var prefix = args[1]?.ToString() ?? "";
            return str.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        };

        _functions["ENDSWITH"] = args =>
        {
            ValidateArgs("ENDSWITH", args, 2);
            var str = args[0]?.ToString() ?? "";
            var suffix = args[1]?.ToString() ?? "";
            return str.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        };

        _functions["INDEXOF"] = args =>
        {
            ValidateArgs("INDEXOF", args, 2);
            var str = args[0]?.ToString() ?? "";
            var search = args[1]?.ToString() ?? "";
            return str.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        };
        
        // ==================== MATH FUNCTIONS ====================
        
        _functions["ROUND"] = args =>
        {
            ValidateArgs("ROUND", args, 1);
            var value = ToDecimal(args[0]);
            var decimals = args.Length > 1 ? Convert.ToInt32(args[1]) : 0;
            return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        };

        _functions["FLOOR"] = args => { ValidateArgs("FLOOR", args, 1); return Math.Floor(ToDecimal(args[0])); };
        _functions["CEILING"] = args => { ValidateArgs("CEILING", args, 1); return Math.Ceiling(ToDecimal(args[0])); };
        _functions["ABS"] = args => { ValidateArgs("ABS", args, 1); return Math.Abs(ToDecimal(args[0])); };

        _functions["POWER"] = args =>
        {
            ValidateArgs("POWER", args, 2);
            var baseVal = ToDouble(args[0]);
            var exponent = ToDouble(args[1]);
            return (decimal)Math.Pow(baseVal, exponent);
        };

        _functions["SQRT"] = args => { ValidateArgs("SQRT", args, 1); return (decimal)Math.Sqrt(ToDouble(args[0])); };
        
        _functions["MIN"] = args =>
        {
            var values = args.Where(a => a != null).Select(ToDecimal);
            return values.Any() ? values.Min() : null;
        };
        
        _functions["MAX"] = args =>
        {
            var values = args.Where(a => a != null).Select(ToDecimal);
            return values.Any() ? values.Max() : null;
        };
        
        _functions["MOD"] = args =>
        {
            ValidateArgs("MOD", args, 2);
            var dividend = ToDecimal(args[0]);
            var divisor = ToDecimal(args[1]);
            return dividend % divisor;
        };
        
        // ==================== DATE/TIME FUNCTIONS ====================
        
        _functions["NOW"] = _ => DateTime.UtcNow;
        _functions["TODAY"] = _ => DateTime.UtcNow.Date;
        _functions["UTCNOW"] = _ => DateTime.UtcNow;
        
        _functions["YEAR"] = args => { ValidateArgs("YEAR", args, 1); return ToDateTime(args[0])?.Year; };
        _functions["MONTH"] = args => { ValidateArgs("MONTH", args, 1); return ToDateTime(args[0])?.Month; };
        _functions["DAY"] = args => { ValidateArgs("DAY", args, 1); return ToDateTime(args[0])?.Day; };
        _functions["HOUR"] = args => { ValidateArgs("HOUR", args, 1); return ToDateTime(args[0])?.Hour; };
        _functions["MINUTE"] = args => { ValidateArgs("MINUTE", args, 1); return ToDateTime(args[0])?.Minute; };
        _functions["SECOND"] = args => { ValidateArgs("SECOND", args, 1); return ToDateTime(args[0])?.Second; };

        _functions["DAYOFWEEK"] = args => { ValidateArgs("DAYOFWEEK", args, 1); return (int?)ToDateTime(args[0])?.DayOfWeek; };
        _functions["DAYOFYEAR"] = args => { ValidateArgs("DAYOFYEAR", args, 1); return ToDateTime(args[0])?.DayOfYear; };

        _functions["ADDDAYS"] = args =>
        {
            ValidateArgs("ADDDAYS", args, 2);
            var date = ToDateTime(args[0]);
            var days = Convert.ToDouble(args[1] ?? 0);
            return date?.AddDays(days);
        };

        _functions["ADDMONTHS"] = args =>
        {
            ValidateArgs("ADDMONTHS", args, 2);
            var date = ToDateTime(args[0]);
            var months = Convert.ToInt32(args[1] ?? 0);
            return date?.AddMonths(months);
        };

        _functions["ADDYEARS"] = args =>
        {
            ValidateArgs("ADDYEARS", args, 2);
            var date = ToDateTime(args[0]);
            var years = Convert.ToInt32(args[1] ?? 0);
            return date?.AddYears(years);
        };

        _functions["ADDHOURS"] = args =>
        {
            ValidateArgs("ADDHOURS", args, 2);
            var date = ToDateTime(args[0]);
            var hours = Convert.ToDouble(args[1] ?? 0);
            return date?.AddHours(hours);
        };

        _functions["ADDMINUTES"] = args =>
        {
            ValidateArgs("ADDMINUTES", args, 2);
            var date = ToDateTime(args[0]);
            var minutes = Convert.ToDouble(args[1] ?? 0);
            return date?.AddMinutes(minutes);
        };

        _functions["DATEDIFF"] = args =>
        {
            ValidateArgs("DATEDIFF", args, 2);
            var date1 = ToDateTime(args[0]);
            var date2 = ToDateTime(args[1]);
            if (date1 == null || date2 == null) return null;
            return (date2.Value - date1.Value).TotalDays;
        };

        _functions["FORMAT"] = args =>
        {
            ValidateArgs("FORMAT", args, 2);
            var value = args[0];
            var format = args[1]?.ToString() ?? "";
            
            return value switch
            {
                DateTime dt => dt.ToString(format),
                DateOnly d => d.ToString(format),
                decimal dec => dec.ToString(format),
                double dbl => dbl.ToString(format),
                int i => i.ToString(format),
                long l => l.ToString(format),
                _ => value?.ToString()
            };
        };
        
        // ==================== LOGIC/NULL FUNCTIONS ====================
        
        _functions["COALESCE"] = args =>
        {
            foreach (var arg in args)
            {
                if (arg != null) return arg;
            }
            return null;
        };
        
        _functions["NULLIF"] = args =>
        {
            ValidateArgs("NULLIF", args, 2);
            var value = args[0];
            var compareValue = args[1];
            return Equals(value, compareValue) ? null : value;
        };

        _functions["IIF"] = args =>
        {
            ValidateArgs("IIF", args, 3);
            var condition = ToBool(args[0]);
            return condition ? args[1] : args[2];
        };

        _functions["ISNULL"] = args => { ValidateArgs("ISNULL", args, 1); return args[0] == null; };
        _functions["ISNOTNULL"] = args => { ValidateArgs("ISNOTNULL", args, 1); return args[0] != null; };
        
        // ==================== GUID FUNCTIONS ====================
        
        _functions["NEWGUID"] = _ => Guid.NewGuid();
        _functions["NEWID"] = _functions["NEWGUID"];
        
        _functions["TOGUID"] = args =>
        {
            ValidateArgs("TOGUID", args, 1);
            var str = args[0]?.ToString();
            return str != null && Guid.TryParse(str, out var guid) ? guid : null;
        };

        // ==================== STRING FUNCTIONS (additional) ====================

        _functions["INSTR"] = args =>
        {
            ValidateArgs("INSTR", args, 2);
            var str = args[0]?.ToString() ?? "";
            var substring = args[1]?.ToString() ?? "";
            if (string.IsNullOrEmpty(substring)) return 0;
            var index = str.IndexOf(substring, StringComparison.OrdinalIgnoreCase);
            return index + 1; // 1-based position, 0 if not found
        };

        // LPAD/RPAD as aliases for PADLEFT/PADRIGHT (grammar uses both)
        _functions["LPAD"] = _functions["PADLEFT"];
        _functions["RPAD"] = _functions["PADRIGHT"];

        // ==================== CONDITIONAL FUNCTIONS ====================

        _functions["IFNULL"] = args =>
        {
            ValidateArgs("IFNULL", args, 2);
            // IFNULL(expr, replacement) - like COALESCE but always 2 args
            return args[0] ?? args[1];
        };

        _functions["DECODE"] = args =>
        {
            // DECODE(expr, search1, result1, search2, result2, ..., default)
            // Min args: 4 (expr, search, result, default) or 3 (expr, search, result)
            if (args.Length < 3) return null;

            var expr = args[0];
            // Iterate pairs: (search, result) starting at index 1
            for (int i = 1; i + 1 < args.Length; i += 2)
            {
                if (Equals(expr, args[i]))
                    return args[i + 1];
            }
            // If odd remaining arg, it's the default
            return args.Length % 2 == 0 ? args[^1] : null;
        };

        // ==================== TYPE CONVERSION FUNCTIONS ====================

        _functions["TO_INTEGER"] = args =>
        {
            ValidateArgs("TO_INTEGER", args, 1);
            var value = args[0];
            if (value == null) return null;
            return value switch
            {
                int i => i,
                long l => (int)l,
                decimal d => (int)d,
                double dbl => (int)dbl,
                float f => (int)f,
                string s => int.TryParse(s, out var result) ? result : (object?)null,
                bool b => b ? 1 : 0,
                _ => null
            };
        };

        _functions["TO_DECIMAL"] = args =>
        {
            ValidateArgs("TO_DECIMAL", args, 1);
            var value = args[0];
            if (value == null) return null;
            return value switch
            {
                decimal d => d,
                int i => (decimal)i,
                long l => (decimal)l,
                double dbl => (decimal)dbl,
                float f => (decimal)f,
                string s => decimal.TryParse(s, out var result) ? result : (object?)null,
                _ => null
            };
        };

        _functions["TO_DATE"] = args =>
        {
            ValidateArgs("TO_DATE", args, 1);
            var value = args[0];
            if (value == null) return null;
            return value switch
            {
                DateTime dt => DateOnly.FromDateTime(dt),
                DateOnly d => d,
                DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
                string s => DateOnly.TryParse(s, out var result) ? result : (object?)null,
                _ => null
            };
        };

        _functions["TO_STRING"] = args =>
        {
            ValidateArgs("TO_STRING", args, 1);
            var value = args[0];
            if (value == null) return null;
            if (args.Length > 1 && args[1] != null)
            {
                // Optional format string
                var format = args[1].ToString()!;
                return value switch
                {
                    DateTime dt => dt.ToString(format),
                    DateOnly d => d.ToString(format),
                    decimal dec => dec.ToString(format),
                    double dbl => dbl.ToString(format),
                    int i => i.ToString(format),
                    long l => l.ToString(format),
                    _ => value.ToString()
                };
            }
            return value.ToString();
        };

        _functions["TO_TIME"] = args =>
        {
            ValidateArgs("TO_TIME", args, 1);
            var value = args[0];
            if (value == null) return null;
            return value switch
            {
                TimeOnly t => t,
                DateTime dt => TimeOnly.FromDateTime(dt),
                string s => TimeOnly.TryParse(s, out var result) ? result : (object?)null,
                _ => null
            };
        };

        _functions["TO_TIMESTAMP"] = args =>
        {
            ValidateArgs("TO_TIMESTAMP", args, 1);
            var value = args[0];
            if (value == null) return null;
            return value switch
            {
                DateTime dt => dt,
                DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                DateTimeOffset dto => dto.UtcDateTime,
                string s => DateTime.TryParse(s, out var result) ? result : (object?)null,
                _ => null
            };
        };

        // ==================== STATISTICAL FUNCTIONS ====================

        _functions["STDDEV"] = args =>
        {
            var values = args.Where(a => a != null).Select(ToDecimal).ToArray();
            if (values.Length < 2) return null;
            var mean = values.Average(v => (double)v);
            var sumSquaredDiffs = values.Sum(v => Math.Pow((double)v - mean, 2));
            return (decimal)Math.Sqrt(sumSquaredDiffs / (values.Length - 1)); // Sample stddev
        };

        _functions["VARIANCE"] = args =>
        {
            var values = args.Where(a => a != null).Select(ToDecimal).ToArray();
            if (values.Length < 2) return null;
            var mean = values.Average(v => (double)v);
            var sumSquaredDiffs = values.Sum(v => Math.Pow((double)v - mean, 2));
            return (decimal)(sumSquaredDiffs / (values.Length - 1)); // Sample variance
        };

        // ==================== DOMAIN-SPECIFIC FUNCTIONS (stubs) ====================

        _functions["CURRENCY_CONVERSION"] = args =>
        {
            // Stub: returns the amount as-is. Real implementation needs rate tables.
            // Args: amount, source_currency, target_currency
            if (args.Length < 1 || args[0] == null) return null;
            _logger.LogWarning("CURRENCY_CONVERSION: No rate configuration available. Returning original amount.");
            return ToDecimal(args[0]);
        };

        _functions["UNIT_CONVERSION"] = args =>
        {
            // Stub: returns the value as-is. Real implementation needs conversion tables.
            // Args: value, source_unit, target_unit
            if (args.Length < 1 || args[0] == null) return null;
            _logger.LogWarning("UNIT_CONVERSION: No unit conversion configuration available. Returning original value.");
            return ToDecimal(args[0]);
        };

        // ==================== MISSING MATH FUNCTIONS ====================

        _functions["TRUNC"] = args =>
        {
            ValidateArgs("TRUNC", args, 1);
            var value = ToDecimal(args[0]);
            return Math.Truncate(value);
        };

        _functions["SIGN"] = args =>
        {
            ValidateArgs("SIGN", args, 1);
            var value = ToDecimal(args[0]);
            return Math.Sign(value);
        };

        _functions["CEIL"] = _functions["CEILING"];

        // ==================== MISSING DATE/TIME FUNCTIONS ====================

        _functions["WEEKOFYEAR"] = args =>
        {
            ValidateArgs("WEEKOFYEAR", args, 1);
            var date = ToDateTime(args[0]);
            if (date == null) return null;
            return System.Globalization.ISOWeek.GetWeekOfYear(date.Value);
        };

        _functions["CURRENT_DATE"] = _ => DateOnly.FromDateTime(DateTime.UtcNow);
        _functions["CURRENT_TIME"] = _ => TimeOnly.FromDateTime(DateTime.UtcNow);
        _functions["CURRENT_TIMESTAMP"] = _ => DateTime.UtcNow;

        // ==================== UNDERSCORE ALIASES (grammar uses underscores) ====================

        _functions["ADD_DAYS"] = _functions["ADDDAYS"];
        _functions["ADD_MONTHS"] = _functions["ADDMONTHS"];
        _functions["ADD_YEARS"] = _functions["ADDYEARS"];
        _functions["PAD_LEFT"] = _functions["PADLEFT"];
        _functions["PAD_RIGHT"] = _functions["PADRIGHT"];

        // ==================== SEQUENCE FUNCTIONS (require runtime provider) ====================

        _functions["NEXT_SEQUENCE"] = args =>
        {
            var seqName = args.Length > 0 ? args[0]?.ToString() : null;
            throw new InvalidOperationException(
                $"NEXT_SEQUENCE('{seqName}'): Sequence functions require a configured ISequenceProvider. " +
                "Register an ISequenceProvider implementation in the DI container.");
        };

        _functions["CURRENT_SEQUENCE"] = args =>
        {
            var seqName = args.Length > 0 ? args[0]?.ToString() : null;
            throw new InvalidOperationException(
                $"CURRENT_SEQUENCE('{seqName}'): Sequence functions require a configured ISequenceProvider. " +
                "Register an ISequenceProvider implementation in the DI container.");
        };

        _functions["FORMAT_SEQUENCE"] = args =>
        {
            // FORMAT_SEQUENCE(pattern, value) — applies pattern formatting
            // e.g. FORMAT_SEQUENCE("INV-{0:D6}", 42) → "INV-000042"
            if (args.Length < 2) return args.Length > 0 ? args[0]?.ToString() : null;
            var pattern = args[0]?.ToString() ?? "{0}";
            var value = args[1];
            try
            {
                return string.Format(pattern, value);
            }
            catch (FormatException)
            {
                return value?.ToString();
            }
        };

        _functions["RESET_SEQUENCE"] = args =>
        {
            var seqName = args.Length > 0 ? args[0]?.ToString() : null;
            throw new InvalidOperationException(
                $"RESET_SEQUENCE('{seqName}'): Sequence functions require a configured ISequenceProvider. " +
                "Register an ISequenceProvider implementation in the DI container.");
        };

        _functions["SET_SEQUENCE"] = args =>
        {
            var seqName = args.Length > 0 ? args[0]?.ToString() : null;
            throw new InvalidOperationException(
                $"SET_SEQUENCE('{seqName}'): Sequence functions require a configured ISequenceProvider. " +
                "Register an ISequenceProvider implementation in the DI container.");
        };

        // Fiscal calendar functions
        _functions["FISCAL_YEAR"] = args =>
        {
            if (args.Length < 1 || args[0] == null) return null;
            var date = ToDateTime(args[0]);
            // Default fiscal year: same as calendar year
            // Override via IFiscalCalendarProvider for custom fiscal calendars
            return date?.Year;
        };

        _functions["FISCAL_PERIOD"] = args =>
        {
            if (args.Length < 1 || args[0] == null) return null;
            var date = ToDateTime(args[0]);
            // Default fiscal period: same as calendar month (1-12)
            // Override via IFiscalCalendarProvider for custom fiscal calendars
            return date?.Month;
        };
    }

    // ==================== HELPER METHODS ====================

    private static decimal ToDecimal(object? value)
    {
        return value switch
        {
            null => 0m,
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            int i => i,
            long l => l,
            string s => decimal.TryParse(s, out var result) ? result : 0m,
            _ => 0m
        };
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0.0,
            double d => d,
            decimal dec => (double)dec,
            float f => f,
            int i => i,
            long l => l,
            string s => double.TryParse(s, out var result) ? result : 0.0,
            _ => 0.0
        };
    }

    private static DateTime? ToDateTime(object? value)
    {
        return value switch
        {
            null => null,
            DateTime dt => dt,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            DateTimeOffset dto => dto.UtcDateTime,
            string s => DateTime.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static bool ToBool(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            string s => bool.TryParse(s, out var result) && result,
            _ => false
        };
    }
}
