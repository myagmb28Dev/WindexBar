using WindexBar.Core.Providers.Codex;

namespace WindexBar.Core.Tests;

public sealed class CodexSessionStateReaderCachingTests
{
    [Fact]
    public void ReusesUnchangedSessionFilesAndInvalidatesCacheAfterAppend()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDirectory = Path.Combine(codexHome, "sessions", "2026", "07", "27");
        Directory.CreateDirectory(sessionDirectory);
        var sessionPath = Path.Combine(sessionDirectory, "rollout-user.jsonl");
        File.WriteAllText(sessionPath, """
        {"timestamp":"2026-07-27T00:00:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-07-27T00:00:01Z","type":"turn_context","payload":{"model":"gpt-5.5","effort":"high"}}
        """);

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CODEX_HOME"] = codexHome
        };
        var first = CodexSessionStateReader.ReadLatestState(environment);
        var unchanged = CodexSessionStateReader.ReadLatestState(environment);

        Assert.NotNull(first?.ActiveModel);
        Assert.Same(first.ActiveModel, unchanged?.ActiveModel);

        File.AppendAllText(sessionPath, Environment.NewLine + """
        {"timestamp":"2026-07-27T00:00:02Z","type":"turn_context","payload":{"model":"gpt-5.6-sol","effort":"xhigh"}}
        """);
        File.SetLastWriteTimeUtc(sessionPath, DateTime.UtcNow.AddSeconds(2));

        var updated = CodexSessionStateReader.ReadLatestState(environment);

        Assert.Equal("gpt-5.6-sol", updated?.ActiveModel?.Model);
        Assert.Equal("xhigh", updated?.ActiveModel?.ReasoningEffort);
        Assert.NotSame(first.ActiveModel, updated?.ActiveModel);
    }
}
