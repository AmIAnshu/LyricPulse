namespace LyricPulse.App.Platform;

public readonly record struct ParsedHotkeyGesture(uint Modifiers, uint VirtualKey, string NormalizedGesture);

/// <summary>
/// Pure Win32 RegisterHotKey gesture parser supporting canonical defaults
/// (Ctrl+Shift+L, Ctrl+Shift+S, Ctrl+Shift+[, Ctrl+Shift+], Ctrl+Shift+G) and custom remappings.
/// </summary>
public static class HotkeyGestureParser
{
    public static bool TryParse(string? gesture, out ParsedHotkeyGesture parsed, out string? errorMessage)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            errorMessage = "Shortcut cannot be empty.";
            return false;
        }

        var rawTokens = Tokenize(gesture.Trim());
        if (rawTokens.Count < 2)
        {
            errorMessage = $"Shortcut '{gesture}' must include at least one modifier (Ctrl, Shift, Alt, or Win) and a key.";
            return false;
        }

        uint modifiers = 0;
        uint virtualKey = 0;
        string? normalizedKey = null;

        foreach (var token in rawTokens)
        {
            if (TryParseModifier(token, out var modFlag))
            {
                modifiers |= modFlag;
                continue;
            }

            if (virtualKey != 0)
            {
                errorMessage = $"Shortcut '{gesture}' specifies more than one non-modifier key.";
                return false;
            }

            if (!TryParseVirtualKey(token, out virtualKey, out normalizedKey))
            {
                errorMessage = $"Unrecognized key '{token}' in shortcut '{gesture}'.";
                return false;
            }
        }

        if (modifiers == 0)
        {
            errorMessage = $"Shortcut '{gesture}' must include at least one modifier (Ctrl, Shift, Alt, or Win).";
            return false;
        }

        if (virtualKey == 0 || normalizedKey is null)
        {
            errorMessage = $"Shortcut '{gesture}' is missing a primary key.";
            return false;
        }

        var normalized = FormatNormalized(modifiers, normalizedKey);
        parsed = new ParsedHotkeyGesture(modifiers | Win32Native.MOD_NOREPEAT, virtualKey, normalized);
        errorMessage = null;
        return true;
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>(4);
        var currentStart = 0;

        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] != '+')
            {
                continue;
            }

            // Allow trailing '+' to represent the Plus key (e.g., "Ctrl+Shift++")
            if (i == input.Length - 1 && i > 0 && input[i - 1] == '+')
            {
                continue;
            }

            var slice = input[currentStart..i].Trim();
            if (!string.IsNullOrEmpty(slice))
            {
                tokens.Add(slice);
            }

            currentStart = i + 1;
        }

        var tail = input[currentStart..].Trim();
        if (!string.IsNullOrEmpty(tail))
        {
            tokens.Add(tail);
        }

        return tokens;
    }

    private static bool TryParseModifier(string token, out uint modifierFlag)
    {
        if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("Control", StringComparison.OrdinalIgnoreCase))
        {
            modifierFlag = Win32Native.MOD_CONTROL;
            return true;
        }

        if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
        {
            modifierFlag = Win32Native.MOD_SHIFT;
            return true;
        }

        if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
        {
            modifierFlag = Win32Native.MOD_ALT;
            return true;
        }

        if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("Meta", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("Super", StringComparison.OrdinalIgnoreCase))
        {
            modifierFlag = Win32Native.MOD_WIN;
            return true;
        }

        modifierFlag = 0;
        return false;
    }

    private static bool TryParseVirtualKey(string token, out uint vk, out string? displayKey)
    {
        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = ch;
                displayKey = ch.ToString();
                return true;
            }

            switch (ch)
            {
                case '[': vk = 0xDB; displayKey = "["; return true;
                case ']': vk = 0xDD; displayKey = "]"; return true;
                case ',': vk = 0xBC; displayKey = ","; return true;
                case '.': vk = 0xBE; displayKey = "."; return true;
                case '-': vk = 0xBD; displayKey = "-"; return true;
                case '=':
                case '+': vk = 0xBB; displayKey = "+"; return true;
                case ';': vk = 0xBA; displayKey = ";"; return true;
                case '/': vk = 0xBF; displayKey = "/"; return true;
                case '\\': vk = 0xDC; displayKey = "\\"; return true;
            }
        }

        if (token.Length == 2 && (token[0] is 'D' or 'd') && token[1] is >= '0' and <= '9')
        {
            vk = token[1];
            displayKey = token[1].ToString();
            return true;
        }

        if (token.Length >= 2 && (token[0] is 'F' or 'f') &&
            int.TryParse(token.AsSpan(1), out var fNum) && fNum is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + (fNum - 1));
            displayKey = $"F{fNum}";
            return true;
        }

        switch (token.ToUpperInvariant())
        {
            case "OEMOPENBRACKETS":
            case "LEFTBRACKET":
                vk = 0xDB; displayKey = "["; return true;
            case "OEMCLOSEBRACKETS":
            case "RIGHTBRACKET":
                vk = 0xDD; displayKey = "]"; return true;
            case "SPACE":
                vk = 0x20; displayKey = "Space"; return true;
            case "LEFT":
                vk = 0x25; displayKey = "Left"; return true;
            case "UP":
                vk = 0x26; displayKey = "Up"; return true;
            case "RIGHT":
                vk = 0x27; displayKey = "Right"; return true;
            case "DOWN":
                vk = 0x28; displayKey = "Down"; return true;
            case "COMMA":
                vk = 0xBC; displayKey = ","; return true;
            case "PERIOD":
                vk = 0xBE; displayKey = "."; return true;
            case "MINUS":
                vk = 0xBD; displayKey = "-"; return true;
            case "PLUS":
            case "OEMPLUS":
                vk = 0xBB; displayKey = "+"; return true;
            default:
                vk = 0;
                displayKey = null;
                return false;
        }
    }

    private static string FormatNormalized(uint modifiers, string key)
    {
        var parts = new List<string>(5);
        if ((modifiers & Win32Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Win32Native.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Win32Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Win32Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(key);
        return string.Join('+', parts);
    }
}
