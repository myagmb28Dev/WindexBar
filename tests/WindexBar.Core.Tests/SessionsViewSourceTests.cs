using System.Runtime.CompilerServices;

namespace WindexBar.Core.Tests;

public sealed class SessionsViewSourceTests
{
    [Fact]
    public void LongSessionNamesMoveToTheirEndOncePerHover()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "Views",
            "SessionsViewControl.cs")));

        Assert.Contains("CreateSessionNameMarquee(session.DisplayName)", source, StringComparison.Ordinal);
        Assert.Contains("nameViewport.PointerEntered", source, StringComparison.Ordinal);
        Assert.Contains("var overflow = Math.Max(0, nameViewport.ScrollableWidth)", source, StringComparison.Ordinal);
        Assert.Contains("To = -overflow", source, StringComparison.Ordinal);
        Assert.Contains("RepeatBehavior = new RepeatBehavior(1)", source, StringComparison.Ordinal);
        Assert.Contains("nameViewport.PointerExited", source, StringComparison.Ordinal);
        Assert.Contains("args.GetCurrentPoint(nameViewport).Position", source, StringComparison.Ordinal);
        Assert.Contains("transform.X = 0", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(
        string relativePath,
        [CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[]
                 {
                     Path.GetDirectoryName(sourceFilePath),
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            var directory = start;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var candidate = Path.Combine(directory, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {sourceFilePath}");
    }
}
