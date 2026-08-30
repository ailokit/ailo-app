using Avalonia;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Ailo.Data;
using System.Globalization;

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
    public const string JobMaxRuntimeKey = "jobs.max-runtime-seconds";
    public const string DefaultTheme = "system";
    public const string DefaultAccentColor = "#0EA5E9";
    public const string DefaultLanguage = "zh-CN";
    public static string DefaultChatShortcut => OperatingSystem.IsMacOS() ? "Meta+Shift+Space" : "Ctrl+Shift+Space";
    public const string DefaultNewChatWindowShortcut = "";
    public static readonly TimeSpan DefaultJobMaxRuntime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MinimumJobMaxRuntime = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumJobMaxRuntime = TimeSpan.FromDays(1);

    /// <summary>Gets a raw setting value, or <see langword="null"/> when the setting has not been saved.</summary>
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => repository.GetAsync(key, cancellationToken);
    /// <summary>Saves a raw setting value using an insert-or-update operation.</summary>
    public Task SaveAsync(string key, string value, CancellationToken cancellationToken = default) => repository.SaveAsync(key, value, cancellationToken);

    /// <summary>Gets the process-wide maximum duration allowed for one scheduled job execution.</summary>
    public async Task<TimeSpan> GetJobMaxRuntimeAsync(CancellationToken cancellationToken = default)
    {
        var value = await GetAsync(JobMaxRuntimeKey, cancellationToken).ConfigureAwait(false);
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return DefaultJobMaxRuntime;
        }

        if (seconds < MinimumJobMaxRuntime.TotalSeconds || seconds > MaximumJobMaxRuntime.TotalSeconds)
        {
            return DefaultJobMaxRuntime;
        }

        var duration = TimeSpan.FromSeconds(seconds);
        return duration >= MinimumJobMaxRuntime && duration <= MaximumJobMaxRuntime
            ? duration
            : DefaultJobMaxRuntime;
    }

    /// <summary>Persists the process-wide maximum duration allowed for one scheduled job execution.</summary>
    public Task SaveJobMaxRuntimeAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ValidateJobMaxRuntime(duration);
        return SaveAsync(
            JobMaxRuntimeKey,
            ((long)duration.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            cancellationToken);
    }

    public static void ValidateJobMaxRuntime(TimeSpan duration)
    {
        if (duration < MinimumJobMaxRuntime || duration > MaximumJobMaxRuntime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                duration,
                $"Scheduled job maximum runtime must be between {MinimumJobMaxRuntime.TotalSeconds:0} seconds and {MaximumJobMaxRuntime.TotalMinutes:0} minutes.");
        }
    }

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
