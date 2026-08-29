using Avalonia.Input;

namespace Ailo.Services;

public static class ShortcutFormatter
{
    public static bool TryParse(string shortcut, out string normalized)
    {
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = Normalize(shortcut);
        return true;
    }

    public static bool TryFormat(Key key, KeyModifiers modifiers, out string shortcut)
    {
        var keyName = key.ToString();
        if (modifiers == KeyModifiers.None && (keyName.Contains("Ctrl", StringComparison.OrdinalIgnoreCase) || keyName.Contains("Alt", StringComparison.OrdinalIgnoreCase) || keyName.Contains("Shift", StringComparison.OrdinalIgnoreCase) || keyName.Contains("Win", StringComparison.OrdinalIgnoreCase)))
        {
            shortcut = string.Empty;
            return false;
        }

        var parts = new List<string>(5);
        if ((modifiers & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & KeyModifiers.Meta) != 0) parts.Add("Meta");
        parts.Add(keyName);
        shortcut = string.Join('+', parts);
        return true;
    }

    public static bool Matches(string shortcut, Key key, KeyModifiers modifiers) =>
        TryFormat(key, modifiers, out var current) && string.Equals(Normalize(shortcut), Normalize(current), StringComparison.OrdinalIgnoreCase);

    public static bool Matches(string shortcut, string current) =>
        string.Equals(Normalize(shortcut), Normalize(current), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => string.Join('+', value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(part => part switch
    {
        "Control" => "Ctrl",
        "Command" => "Meta",
        _ => part
    }));
}
