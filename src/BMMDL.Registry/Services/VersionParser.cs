namespace BMMDL.Registry.Services;

/// <summary>
/// Semantic version parser and comparator.
/// Follows SemVer 2.0.0 specification: MAJOR.MINOR.PATCH
/// </summary>
public class VersionParser
{
    /// <summary>
    /// Parse a semantic version string.
    /// </summary>
    public static SemanticVersion Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be null or empty", nameof(version));

        // Remove leading 'v' if present
        var normalized = version.TrimStart('v', 'V');
        
        var parts = normalized.Split('.');
        if (parts.Length < 1 || parts.Length > 3)
            throw new FormatException($"Invalid version format: {version}. Expected MAJOR.MINOR.PATCH");

        if (!int.TryParse(parts[0], out var major) || major < 0)
            throw new FormatException($"Invalid major version: {parts[0]}");

        var minor = 0;
        if (parts.Length >= 2 && !int.TryParse(parts[1], out minor))
            throw new FormatException($"Invalid minor version: {parts[1]}");

        var patch = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out patch))
            throw new FormatException($"Invalid patch version: {parts[2]}");

        return new SemanticVersion(major, minor, patch);
    }

    /// <summary>
    /// Try to parse a semantic version string.
    /// </summary>
    public static bool TryParse(string version, out SemanticVersion? result)
    {
        try
        {
            result = Parse(version);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    /// <summary>
    /// Compare two versions. Returns:
    /// -1 if v1 < v2
    ///  0 if v1 == v2
    ///  1 if v1 > v2
    /// </summary>
    public static int Compare(string v1, string v2)
    {
        return Parse(v1).CompareTo(Parse(v2));
    }

    /// <summary>
    /// Check if upgrading from v1 to v2 is a breaking (major) change.
    /// </summary>
    public static bool IsBreakingChange(string fromVersion, string toVersion)
    {
        var from = Parse(fromVersion);
        var to = Parse(toVersion);
        return to.Major > from.Major;
    }

    /// <summary>
    /// Bump version based on change category.
    /// </summary>
    public static SemanticVersion Bump(string currentVersion, ChangeCategory category)
    {
        var current = Parse(currentVersion);
        return category switch
        {
            ChangeCategory.Patch => new SemanticVersion(current.Major, current.Minor, current.Patch + 1),
            ChangeCategory.Minor => new SemanticVersion(current.Major, current.Minor + 1, 0),
            ChangeCategory.Major => new SemanticVersion(current.Major + 1, 0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
    }

    /// <summary>
    /// Check if a version satisfies a version range (e.g., ">=1.0.0", "^2.0.0", "~1.2.0").
    /// For exact/plain version ranges, supports prefix matching: "1.0" matches any "1.0.x".
    /// </summary>
    public static bool Satisfies(string version, string range)
    {
        var v = Parse(version);
        var trimmedRange = range.Trim();

        // Exact/prefix match (no operator prefix)
        if (!trimmedRange.StartsWith(">=") && !trimmedRange.StartsWith("<=") &&
            !trimmedRange.StartsWith(">") && !trimmedRange.StartsWith("<") &&
            !trimmedRange.StartsWith("^") && !trimmedRange.StartsWith("~"))
        {
            return SatisfiesExactOrPrefix(v, trimmedRange);
        }

        // Range operators
        if (trimmedRange.StartsWith(">="))
        {
            var rangeVersion = Parse(trimmedRange[2..]);
            return v.CompareTo(rangeVersion) >= 0;
        }
        if (trimmedRange.StartsWith("<="))
        {
            var rangeVersion = Parse(trimmedRange[2..]);
            return v.CompareTo(rangeVersion) <= 0;
        }
        if (trimmedRange.StartsWith(">"))
        {
            var rangeVersion = Parse(trimmedRange[1..]);
            return v.CompareTo(rangeVersion) > 0;
        }
        if (trimmedRange.StartsWith("<"))
        {
            var rangeVersion = Parse(trimmedRange[1..]);
            return v.CompareTo(rangeVersion) < 0;
        }

        // Caret (^) - compatible with major version
        if (trimmedRange.StartsWith("^"))
        {
            var rangeVersion = Parse(trimmedRange[1..]);
            return v.Major == rangeVersion.Major && v.CompareTo(rangeVersion) >= 0;
        }

        // Tilde (~) - compatible with minor version
        if (trimmedRange.StartsWith("~"))
        {
            var rangeVersion = Parse(trimmedRange[1..]);
            return v.Major == rangeVersion.Major &&
                   v.Minor == rangeVersion.Minor &&
                   v.CompareTo(rangeVersion) >= 0;
        }

        return false;
    }

    /// <summary>
    /// For exact/plain version ranges, implements semver-compatible prefix matching.
    /// "1" matches any 1.x.y, "1.0" matches any 1.0.x, "1.0.0" requires exact match.
    /// </summary>
    private static bool SatisfiesExactOrPrefix(SemanticVersion version, string rangeStr)
    {
        // Remove leading 'v'/'V'
        var normalized = rangeStr.TrimStart('v', 'V');
        var parts = normalized.Split('.');

        // Only major specified -> match any with same major
        if (parts.Length == 1)
        {
            var rangeVersion = Parse(normalized);
            return version.Major == rangeVersion.Major;
        }

        // Major.Minor specified -> match any with same major.minor
        if (parts.Length == 2)
        {
            var rangeVersion = Parse(normalized);
            return version.Major == rangeVersion.Major &&
                   version.Minor == rangeVersion.Minor;
        }

        // Full version specified -> exact match
        return version.Equals(Parse(normalized));
    }
}

/// <summary>
/// Represents a semantic version (MAJOR.MINOR.PATCH).
/// </summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public SemanticVersion(int major, int minor = 0, int patch = 0)
    {
        if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
        if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
        if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int CompareTo(SemanticVersion other)
    {
        var majorCompare = Major.CompareTo(other.Major);
        if (majorCompare != 0) return majorCompare;

        var minorCompare = Minor.CompareTo(other.Minor);
        if (minorCompare != 0) return minorCompare;

        return Patch.CompareTo(other.Patch);
    }

    public bool Equals(SemanticVersion other) =>
        Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public override bool Equals(object? obj) =>
        obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor, Patch);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);
    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// Category of version change.
/// </summary>
public enum ChangeCategory
{
    /// <summary>
    /// Backward compatible bug fixes (0.0.x).
    /// </summary>
    Patch,
    
    /// <summary>
    /// Backward compatible new features (0.x.0).
    /// </summary>
    Minor,
    
    /// <summary>
    /// Breaking changes (x.0.0).
    /// </summary>
    Major
}
