using System.Reflection;

namespace WindexBar.Windows;

internal static class AppReleaseVersion
{
    public static string Value { get; } = ReadVersion();
    public static string DisplayValue => $"v{Value}";

    private static string ReadVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppReleaseVersion).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Split('+', 2)[0];
        }

        var version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
