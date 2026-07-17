using WindexBar.Core.Config;
using WindexBar.Core.Updates;

namespace WindexBar.Core.Tests;

public sealed class CodexCliUpdateServiceTests
{
    [Theory]
    [InlineData("codex-cli 0.144.5", 0, 144, 5)]
    [InlineData("rust-v0.145.0-alpha.20", 0, 145, 0)]
    [InlineData("v1.2.3", 1, 2, 3)]
    public void ParsesCodexVersionFromCliAndReleaseText(string value, int major, int minor, int patch)
    {
        Assert.True(CodexCliVersion.TryParse(value, out var version));
        Assert.Equal(new CodexCliVersion(major, minor, patch), version);
    }

    [Fact]
    public async Task UsesFreshLatestVersionCacheWithoutNetworkRequest()
    {
        using var executable = new TemporaryCodexExecutable();
        var source = new FakeVersionSource(new CodexCliVersion(9, 9, 9));
        var runner = new FakeProcessRunner(new CodexCommandResult(0, "codex-cli 0.144.4", ""));
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        var service = CreateService(executable, source, runner, now);
        var config = new CodexUpdateConfig
        {
            LatestVersion = "0.144.5",
            LastCheckedAt = now.AddHours(-1)
        };

        var result = await service.CheckAsync(config, false, CancellationToken.None);

        Assert.Equal(CodexVersionStatus.RecommendedUpdate, result.Status);
        Assert.Equal(new CodexCliVersion(0, 144, 5), result.LatestVersion);
        Assert.True(result.UsedCachedLatestVersion);
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task RefreshesExpiredCacheAndStoresLatestStableVersion()
    {
        using var executable = new TemporaryCodexExecutable();
        var source = new FakeVersionSource(new CodexCliVersion(0, 144, 5));
        var runner = new FakeProcessRunner(new CodexCommandResult(0, "codex-cli 0.144.4", ""));
        var now = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
        var service = CreateService(executable, source, runner, now);
        var config = new CodexUpdateConfig
        {
            LatestVersion = "0.144.3",
            LastCheckedAt = now.AddDays(-2)
        };

        var result = await service.CheckAsync(config, false, CancellationToken.None);

        Assert.Equal(CodexVersionStatus.RecommendedUpdate, result.Status);
        Assert.Equal("0.144.5", config.LatestVersion);
        Assert.Equal(now, config.LastCheckedAt);
        Assert.False(result.UsedCachedLatestVersion);
        Assert.Equal(1, source.CallCount);
    }

    [Fact]
    public async Task RequiresUpdateWhenInstalledVersionIsBelowWindexBarMinimum()
    {
        using var executable = new TemporaryCodexExecutable();
        var source = new FakeVersionSource(new CodexCliVersion(0, 144, 5));
        var runner = new FakeProcessRunner(new CodexCommandResult(0, "codex-cli 0.144.3", ""));
        var service = CreateService(executable, source, runner, DateTimeOffset.UtcNow);

        var result = await service.CheckAsync(new CodexUpdateConfig(), false, CancellationToken.None);

        Assert.Equal(CodexVersionStatus.RequiredUpdate, result.Status);
        Assert.Equal(CodexVersionPolicy.MinimumRequiredVersion, result.RequiredVersion);
    }

    [Fact]
    public async Task VerifiesInstalledVersionAfterUpdateCommand()
    {
        using var executable = new TemporaryCodexExecutable();
        var source = new FakeVersionSource(new CodexCliVersion(0, 144, 5));
        var runner = new FakeProcessRunner(
            new CodexCommandResult(0, "updated", ""),
            new CodexCommandResult(0, "codex-cli 0.144.5", ""));
        var service = CreateService(executable, source, runner, DateTimeOffset.UtcNow);

        var result = await service.UpdateAsync(
            CodexInstallMethodNames.Bun,
            null,
            new CodexCliVersion(0, 144, 5),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CodexCliVersion(0, 144, 5), result.InstalledVersion);
        Assert.Contains("bun install -g @openai/codex@latest", runner.Calls[0].Arguments[^1]);
    }

    [Fact]
    public void ExpandsLatestVersionPlaceholderInCustomCommand()
    {
        var command = CodexCliUpdateService.BuildUpdateCommand(
            CodexInstallMethodNames.Custom,
            "my-updater --version {latestVersion}",
            new CodexCliVersion(0, 144, 5));

        Assert.Equal("powershell.exe", command.Executable);
        Assert.Equal("my-updater --version 0.144.5", command.Arguments[^1]);
    }

    [Theory]
    [InlineData(@"C:\Users\me\.bun\bin\codex.exe", CodexInstallMethodNames.Bun)]
    [InlineData(@"C:\Users\me\AppData\Roaming\npm\codex.cmd", CodexInstallMethodNames.Npm)]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WinGet\Links\codex.exe", CodexInstallMethodNames.WinGet)]
    [InlineData(@"C:\Users\me\.codex\bin\codex.exe", CodexInstallMethodNames.PowerShell)]
    public void DetectsInstallMethodFromExecutablePath(string path, string expected)
    {
        Assert.Equal(expected, CodexCliUpdateService.DetectInstallMethod(path));
    }

    private static CodexCliUpdateService CreateService(
        TemporaryCodexExecutable executable,
        ICodexVersionSource source,
        ICodexProcessRunner runner,
        DateTimeOffset now) =>
        new(source, runner, () => now, executable.Environment);

    private sealed class FakeVersionSource(CodexCliVersion version) : ICodexVersionSource
    {
        public int CallCount { get; private set; }

        public Task<CodexCliVersion> FetchLatestStableVersionAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(version);
        }
    }

    private sealed class FakeProcessRunner(params CodexCommandResult[] results) : ICodexProcessRunner
    {
        private readonly Queue<CodexCommandResult> _results = new(results);

        public List<(string Executable, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<CodexCommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add((executable, arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class TemporaryCodexExecutable : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"WindexBarTests-{Guid.NewGuid():N}");

        public TemporaryCodexExecutable()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "codex.exe"), string.Empty);
            Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PATH"] = _directory,
                ["PATHEXT"] = ".EXE;.CMD"
            };
        }

        public IReadOnlyDictionary<string, string> Environment { get; }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
