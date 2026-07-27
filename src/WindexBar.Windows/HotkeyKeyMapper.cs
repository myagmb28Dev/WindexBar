namespace WindexBar.Windows;

internal static class HotkeyKeyMapper
{
    public static bool TryGetVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = 0;
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.Length is >= 2 and <= 3
            && key[0] == 'F'
            && int.TryParse(key[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = key switch
        {
            "Space" => 0x20,
            "Escape" => 0x1B,
            "Tab" => 0x09,
            "Enter" => 0x0D,
            "Backspace" => 0x08,
            "Insert" => 0x2D,
            "Delete" => 0x2E,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "Up" => 0x26,
            "Down" => 0x28,
            "Left" => 0x25,
            "Right" => 0x27,
            _ => 0
        };
        return virtualKey != 0;
    }

    public static string? GetKeyName(uint virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        return virtualKey switch
        {
            0x20 => "Space",
            0x1B => "Escape",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            _ => null
        };
    }
}
