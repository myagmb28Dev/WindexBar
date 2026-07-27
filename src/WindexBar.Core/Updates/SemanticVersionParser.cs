using System.Globalization;
using System.Text.RegularExpressions;

namespace WindexBar.Core.Updates;

internal static partial class SemanticVersionParser
{
    [GeneratedRegex(
        @"(?<!\d)(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static bool TryParse(
        string? value,
        bool requirePatch,
        out int major,
        out int minor,
        out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern().Match(value);
        return match.Success
            && (!requirePatch || match.Groups["patch"].Success)
            && int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major)
            && int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor)
            && (!match.Groups["patch"].Success
                || int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch));
    }

    public static int Compare(
        int major,
        int minor,
        int patch,
        int otherMajor,
        int otherMinor,
        int otherPatch)
    {
        var majorComparison = major.CompareTo(otherMajor);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = minor.CompareTo(otherMinor);
        return minorComparison != 0 ? minorComparison : patch.CompareTo(otherPatch);
    }
}
