using WindexBar.Core.Config;

namespace WindexBar.Core.Tests;

public sealed class HotkeyShortcutTests
{
    [Theory]
    [InlineData("alt+o", "Alt+O")]
    [InlineData("Ctrl + Shift + F12", "Ctrl+Shift+F12")]
    [InlineData("win + space", "Win+Space")]
    public void NormalizesShortcutText(string value, string expected)
    {
        var parsed = HotkeyShortcut.TryParse(value, out var shortcut);

        Assert.True(parsed);
        Assert.Equal(expected, shortcut!.DisplayText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("O")]
    [InlineData("Alt")]
    [InlineData("Alt+Ctrl")]
    [InlineData("Alt+O+P")]
    public void RejectsIncompleteOrAmbiguousShortcutText(string value)
    {
        Assert.False(HotkeyShortcut.TryParse(value, out _));
    }
}
