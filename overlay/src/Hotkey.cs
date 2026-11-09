namespace LiveBackdrop;

/// <summary>A global key combination, as <c>RegisterHotKey</c> takes it.</summary>
internal readonly record struct Hotkey(uint Modifiers, uint Key)
{
    public const uint Alt = 0x1;
    public const uint Control = 0x2;
    public const uint Shift = 0x4;
    public const uint Windows = 0x8;

    // Holding the keys down does not repeat the toggle.
    public const uint NoRepeat = 0x4000;

    /// <summary>
    /// Reads a combination such as <c>Ctrl+Alt+B</c> or <c>Win+Shift+F9</c>: one or more
    /// modifiers and one key, in any case. Null for an empty, unknown or modifier-less one (a
    /// lone key would be taken from every other app).
    /// </summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        uint modifiers = 0;
        Keys? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= Control;
                    break;
                case "alt":
                    modifiers |= Alt;
                    break;
                case "shift":
                    modifiers |= Shift;
                    break;
                case "win" or "windows":
                    modifiers |= Windows;
                    break;
                default:
                    if (key is not null || !TryParseKey(part, out var parsed))
                    {
                        return null;
                    }

                    key = parsed;
                    break;
            }
        }

        return modifiers == 0 || key is null ? null : new Hotkey(modifiers, (uint)key.Value);
    }

    private static bool TryParseKey(string text, out Keys key)
    {
        // A digit is "D1" to Windows Forms; a bare number would parse as a key code.
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            text = "D" + text;
        }

        key = Keys.None;
        if (text.Length == 0 || char.IsAsciiDigit(text[0]) || !Enum.TryParse(text, ignoreCase: true, out Keys parsed))
        {
            return false;
        }

        // Only plain keys, not modifiers or combinations of flags.
        if (parsed is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            || (parsed & Keys.Modifiers) != 0)
        {
            return false;
        }

        key = parsed;
        return true;
    }
}
