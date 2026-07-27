using WindexBar.Core.Formatting;

namespace WindexBar.Core.Tests;

public sealed class TokenCountFormatterTests
{
    [Theory]
    [InlineData(161_000, "ko", "16\uB9CC 1\uCC9C")]
    [InlineData(258_400, "ko", "25\uB9CC 8\uCC9C")]
    [InlineData(1_610_000, "ko", "161\uB9CC")]
    [InlineData(161_000, "en", "161K")]
    public void FormatsTokenCountsForLanguage(long tokens, string language, string expected)
    {
        Assert.Equal(expected, TokenCountFormatter.Format(tokens, language));
    }
}
