using System.Globalization;
using System.Text.RegularExpressions;

namespace WindexBar.Core.Updates;

public readonly partial record struct CodexCliVersion(int Major, int Minor, int Patch) : IComparable<CodexCliVersion>
{
    [GeneratedRegex(@"(?<!\d)(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static bool TryParse(string? value, out CodexCliVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern().Match(value);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new CodexCliVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(CodexCliVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(CodexCliVersion left, CodexCliVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(CodexCliVersion left, CodexCliVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(CodexCliVersion left, CodexCliVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(CodexCliVersion left, CodexCliVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
