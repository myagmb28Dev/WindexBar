namespace WindexBar.Core.Updates;

public readonly record struct CodexCliVersion(int Major, int Minor, int Patch) : IComparable<CodexCliVersion>
{
    public static bool TryParse(string? value, out CodexCliVersion version)
    {
        version = default;
        if (!SemanticVersionParser.TryParse(value, true, out var major, out var minor, out var patch))
        {
            return false;
        }

        version = new CodexCliVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(CodexCliVersion other) =>
        SemanticVersionParser.Compare(Major, Minor, Patch, other.Major, other.Minor, other.Patch);

    public static bool operator <(CodexCliVersion left, CodexCliVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(CodexCliVersion left, CodexCliVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(CodexCliVersion left, CodexCliVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(CodexCliVersion left, CodexCliVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
