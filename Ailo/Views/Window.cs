using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Ailo;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace Ailo.Views;

/// <summary>
/// Base window with cross-platform title-bar behavior, native Windows 11 chrome enhancements,
/// and DI scope management. Each window owns a dedicated <see cref="IServiceScope"/> that is
/// disposed when the window closes, guaranteeing transient resources are cleaned up promptly.
/// </summary>
public abstract class Window : Avalonia.Controls.Window
{
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int DwmWindowCornerPreferenceRound = 2;

    /// <summary>The DI scope that owns this window and all its transient dependencies.</summary>
    protected IServiceScope? Scope { get; private set; }

    /// <summary>
    /// Sets the DI scope that owns this window. Called by <see cref="Services.WindowNavigationService"/>
    /// immediately after the window is resolved from the scope. The scope is disposed when the window
    /// closes, releasing all transient resources.
    /// </summary>
    public void SetScope(IServiceScope scope) => Scope = scope;

    /// <summary>Gets whether a close request should hide the window unless the application is exiting.</summary>
    protected virtual bool HideOnClose => true;

    protected void ConfigurePlatformTitleBar()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = true;
        if (this.FindControl<StackPanel>("CustomWindowButtons") is { } buttons)
        {
            buttons.IsVisible = false;
        }

        if (this.FindControl<StackPanel>("TitleContent") is { } titleContent)
        {
            titleContent.Margin = new Thickness(72, 0, 12, 0);
        }
    }

    protected void OnMinimizeWindow(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    protected void OnToggleMaximizeWindow(object? sender, RoutedEventArgs e) =>
        SetWindowState(WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);

    protected void OnCloseWindow(object? sender, RoutedEventArgs e)
    {
        if (HideOnClose)
        {
            Hide();
            return;
        }

        Close();
    }

    private void SetWindowState(WindowState state)
    {
        WindowState = state;
    }

    private void UpdateMaximizeIcon()
    {
        if (this.FindControl<MaterialIcon>("MaximizeIcon") is { } maximizeIcon)
        {
            maximizeIcon.Kind = WindowState == WindowState.Maximized
                ? MaterialIconKind.WindowRestore
                : MaterialIconKind.WindowMaximize;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeIcon();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Dispose the ViewModel (DataContext) before the scope so that event
        // unsubscriptions and collection clearing happen while DI is intact.
        if (DataContext is System.IDisposable disposable)
        {
            disposable.Dispose();
            DataContext = null;
        }
        DisposeScope();
        base.OnClosed(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (HideOnClose && Application.Current is not App { IsExiting: true })
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>Disposes the dedicated DI scope, releasing all transient resources.</summary>
    private void DisposeScope()
    {
        Scope?.Dispose();
        Scope = null;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateMaximizeIcon();
        EnableNativeWindowChrome();
    }

    private void EnableNativeWindowChrome()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { Handle: var handle })
        {
            return;
        }

        // Keep the client-drawn title bar while asking DWM to render the outer
        // frame. DWM then owns the shadow, DPI scaling and Windows 11 corners.
        var margins = new Margins(1, 1, 1, 1);
        _ = DwmExtendFrameIntoClientArea(handle, in margins);

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreferenceAttribute,
            in cornerPreference,
            sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Margins(int Left, int Right, int Top, int Bottom);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint windowHandle, in Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        in int attributeValue,
        int attributeSize);
}
