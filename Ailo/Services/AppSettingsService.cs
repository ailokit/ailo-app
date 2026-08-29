using Avalonia;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Ailo.Data;

namespace Ailo.Services;

/// <summary>Reads persistent application preferences and applies the UI-affecting subset to Avalonia.</summary>
public sealed class AppSettingsService(AppSettingRepository repository)
{
    public const string ThemeKey = "theme";
    public const string AccentColorKey = "accent-color";
    public const string LanguageKey = "language";
    public const string ChatShortcutKey = "chat.shortcut";
    public const string NewChatWindowShortcutKey = "chat.new-window-shortcut";
    public const string EnabledToolsKey = "ai.enabled-tools";
    public const string ShellToolKey = "ai.shell-tool";
    public const string ShellToolEnabledKey = "ai.shell-tool-enabled";
    public const string DefaultTheme = "system";
    public const string DefaultAccentColor = "#0EA5E9";
    public const string DefaultLanguage = "zh-CN";
    public static string DefaultChatShortcut => OperatingSystem.IsMacOS() ? "Meta+Shift+Space" : "Ctrl+Shift+Space";
    public const string DefaultNewChatWindowShortcut = "";

    /// <summary>Gets a raw setting value, or <see langword="null"/> when the setting has not been saved.</summary>
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => repository.GetAsync(key, cancellationToken);
    /// <summary>Saves a raw setting value using an insert-or-update operation.</summary>
    public Task SaveAsync(string key, string value, CancellationToken cancellationToken = default) => repository.SaveAsync(key, value, cancellationToken);

    /// <summary>Applies a persisted theme key to the current Avalonia application.</summary>
    public static void ApplyTheme(string theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>Updates both Fluent palettes so switching themes preserves the selected accent color.</summary>
    public static void ApplyAccentColor(string hex)
    {
        if (Application.Current is null || !TryParseColor(hex, out var color))
        {
            return;
        }

        var fluentTheme = Application.Current.Styles.OfType<FluentTheme>().FirstOrDefault();
        if (fluentTheme is null)
        {
            return;
        }

        fluentTheme.Palettes[ThemeVariant.Light].Accent = color;
        fluentTheme.Palettes[ThemeVariant.Dark].Accent = color;
    }

    public static bool TryParseColor(string? value, out Color color)
    {
        return Color.TryParse(value, out color) && color.A == byte.MaxValue;
    }
}
