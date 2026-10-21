namespace BlazorTestPlatform;

/// <summary>
/// Key names accepted by <c>PressAsync</c>. They follow the DOM <c>KeyboardEvent.key</c> values used by Playwright;
/// combine with <c>+</c> for chords, e.g. <c>Keys.Chord(Keys.Shift, Keys.Tab)</c>.
/// </summary>
public static class Keys
{
    /// <summary>Enter.</summary>
    public const string Enter = "Enter";

    /// <summary>Tab.</summary>
    public const string Tab = "Tab";

    /// <summary>Escape.</summary>
    public const string Escape = "Escape";

    /// <summary>Space bar.</summary>
    public const string Space = "Space";

    /// <summary>Backspace.</summary>
    public const string Backspace = "Backspace";

    /// <summary>Delete.</summary>
    public const string Delete = "Delete";

    /// <summary>Arrow up.</summary>
    public const string ArrowUp = "ArrowUp";

    /// <summary>Arrow down.</summary>
    public const string ArrowDown = "ArrowDown";

    /// <summary>Arrow left.</summary>
    public const string ArrowLeft = "ArrowLeft";

    /// <summary>Arrow right.</summary>
    public const string ArrowRight = "ArrowRight";

    /// <summary>Home.</summary>
    public const string Home = "Home";

    /// <summary>End.</summary>
    public const string End = "End";

    /// <summary>Page up.</summary>
    public const string PageUp = "PageUp";

    /// <summary>Page down.</summary>
    public const string PageDown = "PageDown";

    /// <summary>Shift modifier.</summary>
    public const string Shift = "Shift";

    /// <summary>Control modifier.</summary>
    public const string Control = "Control";

    /// <summary>Alt modifier.</summary>
    public const string Alt = "Alt";

    /// <summary>Meta modifier.</summary>
    public const string Meta = "Meta";

    /// <summary>Builds a chord such as <c>Shift+Tab</c>.</summary>
    public static string Chord(params string[] keys) => string.Join('+', keys);

    /// <summary>Splits a chord into modifiers and the main key.</summary>
    public static (Drivers.KeyModifiers Modifiers, string Key) Parse(string chord)
    {
        ArgumentException.ThrowIfNullOrEmpty(chord);
        if (chord == "+")
        {
            return (Drivers.KeyModifiers.None, "+");
        }

        var parts = chord.Split('+');
        var modifiers = Drivers.KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i] switch
            {
                Shift => Drivers.KeyModifiers.Shift,
                Control or "Ctrl" => Drivers.KeyModifiers.Control,
                Alt => Drivers.KeyModifiers.Alt,
                Meta or "Cmd" or "ControlOrMeta" => Drivers.KeyModifiers.Meta,
                _ => throw new FormatException($"Unknown modifier '{parts[i]}' in '{chord}'."),
            };
        }

        return (modifiers, parts[^1].Length == 0 ? "+" : parts[^1]);
    }

    /// <summary>The DOM <c>key</c> value for a key name (<c>Space</c> becomes <c>" "</c>).</summary>
    public static string ToDomKey(string key) => key == Space ? " " : key;

    /// <summary>The DOM <c>code</c> value for a key name.</summary>
    public static string ToDomCode(string key) => key switch
    {
        Space or " " => "Space",
        { Length: 1 } when char.IsAsciiLetter(key[0]) => "Key" + char.ToUpperInvariant(key[0]),
        { Length: 1 } when char.IsAsciiDigit(key[0]) => "Digit" + key,
        _ => key,
    };

    /// <summary>True for keys that produce a character (fire <c>keypress</c> and input).</summary>
    public static bool IsPrintable(string key) => key.Length == 1 || key == Space;
}
