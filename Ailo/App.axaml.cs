using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Ailo.Composition;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Jobs;
using Ailo.Services;
using Ailo.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ailo.Views;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ailo;

public partial class App : Application
{
    // The development build deliberately has a different assembly identity.
    // Resolve tray assets from the current assembly so both identities work.
    private static readonly Uri DarkTrayIconUri = CreateAssetUri("Assets/ailo-tray.png");
    private static readonly Uri LightTrayIconUri = CreateAssetUri("Assets/ailo-tray-light.png");
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private IPlatformSettings? _platformSettings;
    private TrayIcon? _trayIcon;
    private LocalizationService? _localization;
    private bool _updateDialogOpen;
    internal bool IsExiting { get; private set; }
    public override void Initialize()
    {
        MarkdownView.ConfigurePipeline();
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _platformSettings = TryGetFeature(typeof(IPlatformSettings)) as IPlatformSettings;
        if (_platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged += (_, _) => UpdateTrayIcon();
        }

        _services = new ServiceCollection().AddAiloApplication().BuildServiceProvider();
        _logger = _services.GetRequiredService<ILogger<App>>();
        ExceptionLogger.Configure(_services.GetRequiredService<ILoggerFactory>());
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        _logger.LogInformation("Ailo is starting");

        try
        {
            _services.InitializeAiloDatabase();
            _services.GetRequiredService<CronJobScheduler>().StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "Ailo failed during startup");
            throw;
        }
        ApplyStoredTheme(_services.GetRequiredService<AppSettingsService>());
        _localization = _services.GetRequiredService<LocalizationService>();
        InitializeTrayMenuLocalization();
        UpdateTrayIcon();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode =  ShutdownMode.OnExplicitShutdown;
            
            var updateService = _services.GetRequiredService<IUpdateService>();
            updateService.UpdateAvailable += OnUpdateAvailable;

            _ = _services.GetRequiredService<AppState>().LoadAsync();

            // Open the first chat window on startup.
            // Hotkey registration and ViewModel initialization are handled inside ChatWindow.OnOpened.
            _services.GetRequiredService<ChatWindowManager>().Show();

            // Update check runs on startup, independent of any window.
            _ = CheckForUpdatesAsync(updateService);

            desktop.Exit += (_, _) =>
            {
                IsExiting = true;
                _logger?.LogInformation("Ailo is shutting down");
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
                try
                {
                    _services.GetRequiredService<CronJobScheduler>().StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                    _services.GetRequiredService<IGlobalHotKeyService>().Dispose();
                    _services.Dispose();
                }
                catch (Exception exception)
                {
                    _logger?.LogWarning(exception, "An error occurred while disposing application services");
                }
                finally
                {
                    ExceptionLogger.Configure(null);
                    _services = null;
                    _logger = null;
                }

                // Avalonia NativeAOT currently releases its native dispatcher from
                // a C++ static destructor after the .NET runtime has begun shutting
                // down. That callback crosses into an already stopped runtime and
                // aborts the process on macOS. All managed resources are disposed
                // above, so bypass the faulty atexit/static-destructor phase only
                // for macOS NativeAOT builds.
                if (OperatingSystem.IsMacOS() && !RuntimeFeature.IsDynamicCodeSupported)
                {
                    ExitImmediately(0);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyStoredTheme(AppSettingsService settings)
    {
        // Apply the persisted choice before any window is constructed. Loading it from the
        // MainWindow.Opened event lets the system theme render briefly on the first frame.
        var theme = settings.GetAsync(AppSettingsService.ThemeKey).GetAwaiter().GetResult()
            ?? AppSettingsService.DefaultTheme;
        AppSettingsService.ApplyTheme(theme);

        var accent = settings.GetAsync(AppSettingsService.AccentColorKey).GetAwaiter().GetResult()
            ?? AppSettingsService.DefaultAccentColor;
        AppSettingsService.ApplyAccentColor(accent);
    }

    private void InitializeTrayMenuLocalization()
    {
        if (_localization is null)
            return;

        // Load the persisted language early so tray menu text is correct before any window opens.
        var language = _services?.GetRequiredService<AppSettingsService>()
            .GetAsync(AppSettingsService.LanguageKey).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(language))
        {
            _localization.SetLanguage(language);
        }

        ApplyTrayMenuText();
        _localization.PropertyChanged += (_, _) => ApplyTrayMenuText();
    }

    private void ApplyTrayMenuText()
    {
        if (_localization is null)
            return;

        var trayIcon = _trayIcon ?? TrayIcon.GetIcons(this)?.FirstOrDefault();
        if (trayIcon?.Menu is not NativeMenu menu)
            return;

        // Index 0: TrayShowChatItem, Index 1: TrayShowSettingsItem, Index 2: separator, Index 3: TrayExitItem
        if (menu.Items.Count >= 1 && menu.Items[0] is NativeMenuItem showChat)
            showChat.Header = _localization["TrayShowChat"];
        if (menu.Items.Count >= 2 && menu.Items[1] is NativeMenuItem showSettings)
            showSettings.Header = _localization["TrayShowSettings"];
        if (menu.Items.Count >= 4 && menu.Items[3] is NativeMenuItem exit)
            exit.Header = _localization["TrayExit"];

        trayIcon.ToolTipText = _localization["AppTitle"];
    }

    private void UpdateTrayIcon()
    {
        _trayIcon ??= TrayIcon.GetIcons(this)?.FirstOrDefault();
        if (_trayIcon is null)
        {
            return;
        }

        // Native trays do not inherit Avalonia brushes. Use the OS theme rather than the application's selected theme.
        var systemTheme = _platformSettings?.GetColorValues().ThemeVariant;
        var iconUri = systemTheme == PlatformThemeVariant.Dark ? LightTrayIconUri : DarkTrayIconUri;
        using var stream = AssetLoader.Open(iconUri);
        _trayIcon.Icon = new WindowIcon(new Bitmap(stream));
    }

    private static Uri CreateAssetUri(string path) => new($"avares://{typeof(App).Assembly.GetName().Name}/{path}");

    private async Task CheckForUpdatesAsync(IUpdateService updateService)
    {
        try
        {
            await updateService.CheckForUpdateAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "The update check failed");
            // Update checks are best-effort and must not interfere with startup.
        }
    }

    private void OnUpdateAvailable(object? sender, AppUpdateInfo update)
    {
        if (sender is IUpdateService updates)
            Dispatcher.UIThread.Post(() => _ = PromptAndDownloadUpdateAsync(updates, update));
    }

    private async Task PromptAndDownloadUpdateAsync(IUpdateService updates, AppUpdateInfo update)
    {
        if (!await ShowUpdateDialogAsync(updates, update, Ailo.Views.UpdateDialogMode.Download))
            return;

        _ = DownloadUpdateAsync(updates, update);
    }

    private async Task DownloadUpdateAsync(IUpdateService updates, AppUpdateInfo update)
    {
        try
        {
            var downloadedFilePath = await updates.DownloadAsync(update).ConfigureAwait(false);
            if (Application.Current is App app)
                Dispatcher.UIThread.Post(() => _ = app.ShowUpdateDialogAsync(updates, update, Ailo.Views.UpdateDialogMode.Install, downloadedFilePath));
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Downloading update {Version} failed", update.LatestVersion);
            // Download errors are published by UpdateService and shown in the About page.
        }
    }

    private async Task<bool> ShowUpdateDialogAsync(IUpdateService updates, AppUpdateInfo update, UpdateDialogMode mode, string? downloadedFilePath = null)
    {
        if (_updateDialogOpen || _services is null || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        var owner = desktop.Windows.FirstOrDefault(window => window.IsActive && window.IsVisible)
            ?? desktop.Windows.LastOrDefault(window => window.IsVisible)
            ?? desktop.MainWindow;
        if (owner is null || !owner.IsVisible)
        {
            return false;
        }

        _updateDialogOpen = true;
        var disabledWindows = desktop.Windows
            .Where(window => !ReferenceEquals(window, owner) && window.IsVisible && window.IsEnabled)
            .ToArray();
        try
        {
            foreach (var window in disabledWindows)
                window.IsEnabled = false;

            var localization = _services.GetRequiredService<LocalizationService>();
            var dialog = new UpdateDialog(update, localization, mode);
            dialog.Opened += (_, _) =>
            {
                dialog.Activate();
                dialog.Focus();
            };
            var confirmed = await dialog.ShowDialog<bool>(owner);
            if (confirmed && mode == Ailo.Views.UpdateDialogMode.Install && downloadedFilePath is not null)
            {
                updates.LaunchInstaller(downloadedFilePath);
                desktop.Shutdown();
            }
            return confirmed;
        }
        finally
        {
            foreach (var window in disabledWindows)
                window.IsEnabled = true;
            _updateDialogOpen = false;
        }
    }

    private void OnTrayShowChat(object? sender, EventArgs e) => _services?.GetRequiredService<ChatWindowManager>().Show();

    private void OnTrayShowSettings(object? sender, EventArgs e) => _services?.GetRequiredService<IWindowNavigationService>().ShowSettings();

    private void OnTrayExit(object? sender, EventArgs e)
    {
        IsExiting = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        if (eventArgs.ExceptionObject is Exception exception)
        {
            _logger?.LogCritical(exception, "An unhandled application exception occurred; terminating: {IsTerminating}", eventArgs.IsTerminating);
        }
        else
        {
            _logger?.LogCritical("An unhandled non-Exception error occurred; terminating: {IsTerminating}", eventArgs.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        _logger?.LogError(eventArgs.Exception, "An unobserved task exception occurred");
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        _logger?.LogCritical(eventArgs.Exception, "An unhandled UI-thread exception occurred");
    }

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_exit")]
    private static extern void ExitImmediately(int status);
}
