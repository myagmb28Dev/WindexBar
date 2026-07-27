using WindexBar.Core.Updates;

namespace WindexBar.Core.Tests;

public sealed class VersionParsingTests
{
    [Fact]
    public void AppVersionAllowsMissingPatchWhileCliVersionRequiresIt()
    {
        Assert.True(AppVersion.TryParse("v2.2", out var appVersion));
        Assert.Equal(new AppVersion(2, 2, 0), appVersion);
        Assert.False(CodexCliVersion.TryParse("v2.2", out _));
    }

    [Fact]
    public void AppAndCliVersionsUseTheSameComponentOrdering()
    {
        Assert.True(new AppVersion(2, 10, 0) > new AppVersion(2, 9, 99));
        Assert.True(new CodexCliVersion(2, 10, 0) > new CodexCliVersion(2, 9, 99));
    }
}
