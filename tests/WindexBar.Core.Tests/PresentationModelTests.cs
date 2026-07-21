using WindexBar.Core.Models;
using WindexBar.Core.Presentation;

namespace WindexBar.Core.Tests;

public sealed class PresentationModelTests
{
    [Fact]
    public void HudModelSelectsActiveModelLimitsAndKeepsErrorSeparateFromDisabledMessage()
    {
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var snapshot = new UsageSnapshot(
            new RateWindow(10, 300, now.AddHours(1), null),
            new RateWindow(20, 10080, now.AddDays(1), null),
            null,
            now,
            new ProviderIdentitySnapshot(UsageProvider.Codex, "me@example.com", null, "chatgpt"),
            [new ModelUsageSnapshot(
                "gpt-5.6",
                new RateWindow(25, 300, now.AddHours(2), null),
                new RateWindow(40, 10080, now.AddDays(2), null))],
            new CodexModelSelection("gpt-5.6-high", "high", "fast", "GPT 5.6", now));

        var model = HudDisplayModelFactory.Create(snapshot, "network error", true, "ko", now);

        Assert.Equal("GPT 5.6", model.Header);
        Assert.Equal("75%", model.Current.Percent);
        Assert.Equal("60%", model.Weekly.Percent);
        Assert.Contains("사용", model.Current.Detail);
        Assert.Equal("제공자 비활성화", model.Meta);
        Assert.Equal("network error", model.Error);
        Assert.Equal("me@example.com (chatgpt)", model.Account);
        Assert.True(model.IsFastServiceTier);
    }

    [Fact]
    public void SessionModelGroupsAndSortsProjectsWithoutDependingOnWinUi()
    {
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var usage = new TokenUsageSnapshot(
            new TokenUsageBreakdown(80, 0, 20, 0, 100),
            new TokenUsageBreakdown(40, 0, 10, 0, 50),
            200,
            now);
        var sessions = new[]
        {
            new CodexSessionUsageSnapshot("general-session", null, null, usage, now),
            new CodexSessionUsageSnapshot("project-session", "Refactor UI", @"D:\Codes\WindexBar", usage, now.AddMinutes(1))
        };

        var projectFirst = SessionListViewModelFactory.Create(sessions, true, "ko");
        var generalFirst = SessionListViewModelFactory.Create(sessions, false, "ko");

        Assert.Equal("컨텍스트", projectFirst.ContextLabel);
        Assert.Equal("WindexBar", projectFirst.Projects[0].ProjectName);
        Assert.Equal("일반 세션", generalFirst.Projects[0].ProjectName);
        Assert.Equal("Refactor UI", projectFirst.Projects[0].Sessions[0].DisplayName);
        Assert.Equal(@"D:\Codes\WindexBar", projectFirst.Projects[0].Sessions[0].ProjectPath);
        Assert.Equal(now.AddMinutes(1), projectFirst.Projects[0].Sessions[0].UpdatedAt);
        Assert.Equal("25%", projectFirst.Projects[0].Sessions[0].ContextPercentText);
        Assert.DoesNotContain("세션 합계", projectFirst.Projects[0].Sessions[0].TokenDetails);
        Assert.Contains("세션 합계", projectFirst.Projects[0].Sessions[0].DetailedTokenDetails);
    }

    [Theory]
    [InlineData("en", "No project")]
    [InlineData("ko", "\uD504\uB85C\uC81D\uD2B8 \uC5C6\uC74C")]
    public void SessionModelLocalizesDefaultSessionDisplayNameWithoutChangingItsKey(
        string language,
        string expectedName)
    {
        var now = DateTimeOffset.UnixEpoch;
        var usage = new TokenUsageSnapshot(
            new TokenUsageBreakdown(80, 0, 20, 0, 100),
            null,
            200,
            now);
        var sessions = new[]
        {
            new CodexSessionUsageSnapshot(
                "default-session",
                "Conversation",
                @"C:\Users\Tester\Documents\Codex\2026-07-21\default-session",
                usage,
                now)
        };

        var model = SessionListViewModelFactory.Create(sessions, true, language);

        var project = Assert.Single(model.Projects);
        Assert.Equal("default-session", project.Key);
        Assert.Equal(expectedName, project.ProjectName);
        Assert.True(project.IsNonProject);
    }

    [Fact]
    public void HudModelKeepsCodexHeaderWhenNoRateLimitWindowCanBeShown()
    {
        var now = DateTimeOffset.UnixEpoch;
        var snapshot = new UsageSnapshot(
            null,
            null,
            null,
            now,
            null,
            ActiveModel: new CodexModelSelection("gpt-5.6", null, null, "GPT 5.6", now));

        var model = HudDisplayModelFactory.Create(snapshot, null, false, "en", now);

        Assert.Equal("Codex", model.Header);
        Assert.Equal("unknown", model.Current.Percent);
    }
}
