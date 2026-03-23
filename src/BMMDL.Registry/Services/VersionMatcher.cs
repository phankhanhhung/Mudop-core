using BMMDL.Registry.Entities;

namespace BMMDL.Registry.Services;

/// <summary>
/// Utility for semantic version range matching.
/// </summary>
public static class VersionMatcher
{
    /// <summary>
    /// Checks if a module's version satisfies the given version range.
    /// </summary>
    public static bool Satisfies(Module module, string versionRange)
    {
        var range = ParseVersionRange(versionRange);
        return MatchesRange(module, range);
    }

    /// <summary>
    /// Parses a version range string (e.g., ">=1.0.0", "^2.0.0", "~1.2.0", "1.0").
    /// Tracks how many version components were explicitly specified so that
    /// partial versions like "1.0" match any "1.0.x" (prefix matching).
    /// </summary>
    public static VersionRange ParseVersionRange(string range)
    {
        var result = new VersionRange();

        if (string.IsNullOrEmpty(range))
            return result;

        var trimmed = range.Trim();

        // Remove leading 'v' or 'V' if present (e.g., "v1.0" -> "1.0")
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            // But only if it's not a range operator prefix
            if (trimmed.Length > 1 && char.IsDigit(trimmed[1]))
            {
                trimmed = trimmed[1..];
            }
        }

        // Handle caret (^) - compatible with major version
        if (trimmed.StartsWith('^'))
        {
            result.Type = RangeType.Caret;
            trimmed = trimmed[1..].Trim('"');
        }
        // Handle tilde (~) - compatible with minor version
        else if (trimmed.StartsWith('~'))
        {
            result.Type = RangeType.Tilde;
            trimmed = trimmed[1..].Trim('"');
        }
        // Handle >=
        else if (trimmed.StartsWith(">="))
        {
            result.Type = RangeType.GreaterOrEqual;
            trimmed = trimmed[2..].Trim().Trim('"');
        }
        // Handle >
        else if (trimmed.StartsWith('>'))
        {
            result.Type = RangeType.Greater;
            trimmed = trimmed[1..].Trim().Trim('"');
        }
        // Exact match (or prefix match if fewer than 3 parts)
        else
        {
            result.Type = RangeType.Exact;
            trimmed = trimmed.Trim('"');
        }

        // Remove leading 'v'/'V' again after stripping operator (e.g., ">=v1.0.0")
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        // Parse version parts and track how many were specified
        var parts = trimmed.Split('.');
        result.SpecifiedParts = parts.Length;

        if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
            result.Major = major;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            result.Minor = minor;
        if (parts.Length >= 3 && int.TryParse(parts[2].Split('-')[0], out var patch))
            result.Patch = patch;

        return result;
    }

    /// <summary>
    /// Checks if a module's version matches the given version range.
    /// For Exact type with partial versions (e.g., "1.0" with only 2 parts specified),
    /// uses prefix matching: "1.0" matches "1.0.0", "1.0.1", "1.0.99", etc.
    /// </summary>
    public static bool MatchesRange(Module module, VersionRange range)
    {
        return range.Type switch
        {
            RangeType.Exact => MatchesExactOrPrefix(module, range),

            RangeType.Caret => module.VersionMajor == range.Major &&
                               (module.VersionMinor > range.Minor ||
                                (module.VersionMinor == range.Minor && module.VersionPatch >= range.Patch)),

            RangeType.Tilde => module.VersionMajor == range.Major &&
                               module.VersionMinor == range.Minor &&
                               module.VersionPatch >= range.Patch,

            RangeType.GreaterOrEqual => module.VersionMajor > range.Major ||
                                        (module.VersionMajor == range.Major && module.VersionMinor > range.Minor) ||
                                        (module.VersionMajor == range.Major && module.VersionMinor == range.Minor && module.VersionPatch >= range.Patch),

            RangeType.Greater => module.VersionMajor > range.Major ||
                                 (module.VersionMajor == range.Major && module.VersionMinor > range.Minor) ||
                                 (module.VersionMajor == range.Major && module.VersionMinor == range.Minor && module.VersionPatch > range.Patch),

            _ => true
        };
    }

    /// <summary>
    /// For Exact range type, implements semver-compatible prefix matching.
    /// If only major is specified ("1"), matches any 1.x.y.
    /// If major.minor is specified ("1.0"), matches any 1.0.x.
    /// If all three parts are specified ("1.0.0"), requires exact match.
    /// </summary>
    private static bool MatchesExactOrPrefix(Module module, VersionRange range)
    {
        // Only major specified (e.g., "1") -> match any 1.x.y
        if (range.SpecifiedParts <= 1)
        {
            return module.VersionMajor == range.Major;
        }

        // Major.Minor specified (e.g., "1.0") -> match any 1.0.x
        if (range.SpecifiedParts == 2)
        {
            return module.VersionMajor == range.Major &&
                   module.VersionMinor == range.Minor;
        }

        // Full version specified (e.g., "1.0.0") -> exact match
        return module.VersionMajor == range.Major &&
               module.VersionMinor == range.Minor &&
               module.VersionPatch == range.Patch;
    }
}

public class VersionRange
{
    public RangeType Type { get; set; } = RangeType.Exact;
    public int Major { get; set; }
    public int Minor { get; set; }
    public int Patch { get; set; }

    /// <summary>
    /// How many version components were explicitly specified in the range string.
    /// 1 = major only ("1"), 2 = major.minor ("1.0"), 3 = full ("1.0.0").
    /// Used for prefix matching in Exact mode.
    /// </summary>
    public int SpecifiedParts { get; set; } = 3;
}

public enum RangeType
{
    Exact,
    Caret,    // ^1.0.0 - compatible with major
    Tilde,    // ~1.0.0 - compatible with minor
    GreaterOrEqual,
    Greater
}
