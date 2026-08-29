using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Ailo.Localization;
using System.ComponentModel;

namespace Ailo.Views;

/// <summary>Displays an attachment at a user-controlled zoom level.</summary>
internal partial class ImageViewerWindow : Avalonia.Controls.Window
{
    private const double MinimumZoom = 0.1;
    private const double MaximumZoom = 8;
    private const double ZoomStep = 1.25;

    private readonly Bitmap _bitmap;
    private readonly Size _imageSize;
    private readonly LocalizationService _localization;
    private readonly string _fileName;
    private double _zoom = 1;
    private double _pinchStartZoom = 1;
    private bool _isPinching;
    private bool _isFitted;

    private ImageViewerWindow(string filePath, LocalizationService localization)
    {
        _bitmap = new Bitmap(filePath);
        _imageSize = _bitmap.Size;
        _localization = localization;
        _fileName = Path.GetFileName(filePath);

        InitializeComponent();
        DataContext = localization;
        UpdateTitle();
        _localization.PropertyChanged += OnLocalizationChanged;
        PreviewImage.Source = _bitmap;
        ApplyZoom(1);
    }

    public static void Open(Avalonia.Controls.Window? owner, string filePath, LocalizationService localization)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        var viewer = new ImageViewerWindow(filePath, localization);
        if (owner is null)
        {
            viewer.Show();
        }
        else
        {
            viewer.Show(owner);
        }
    }

    private void OnOpened(object? sender, EventArgs e) => FitToWindow();

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isFitted)
        {
            FitToWindow();
        }
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e)
    {
        _isFitted = false;
        ApplyZoom(_zoom * ZoomStep);
    }

    private void OnZoomOut(object? sender, RoutedEventArgs e)
    {
        _isFitted = false;
        ApplyZoom(_zoom / ZoomStep);
    }

    private void OnActualSize(object? sender, RoutedEventArgs e)
    {
        _isFitted = false;
        ApplyZoom(1);
    }

    private void OnFitToWindow(object? sender, RoutedEventArgs e) => FitToWindow();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnImagePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return;
        }

        _isFitted = false;
        ApplyZoom(e.Delta.Y > 0 ? _zoom * ZoomStep : _zoom / ZoomStep);
        e.Handled = true;
    }

    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        // macOS emits this for a two-finger trackpad pinch. Avalonia exposes
        // the native magnification delta through the Y component.
        _isFitted = false;
        ApplyZoom(_zoom * (1 + e.Delta.Y));
        e.Handled = true;
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        // Also support true multi-touch input. Pinch scale is relative to the
        // moment the gesture began, hence the saved starting zoom.
        if (!_isPinching)
        {
            _pinchStartZoom = _zoom;
            _isPinching = true;
        }

        _isFitted = false;
        ApplyZoom(_pinchStartZoom * e.Scale);
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _pinchStartZoom = _zoom;
        _isPinching = false;
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _localization.PropertyChanged -= OnLocalizationChanged;
        _bitmap.Dispose();
        base.OnClosed(e);
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e) => UpdateTitle();

    private void UpdateTitle() => Title = string.Format(_localization["ImagePreviewTitle"], _fileName);

    private void FitToWindow()
    {
        var availableWidth = Math.Max(1, ImageScrollViewer.Bounds.Width - 24);
        var availableHeight = Math.Max(1, ImageScrollViewer.Bounds.Height - 24);
        var fitZoom = Math.Min(1, Math.Min(availableWidth / _imageSize.Width, availableHeight / _imageSize.Height));
        ApplyZoom(fitZoom);
        _isFitted = true;
    }

    private void ApplyZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        PreviewImage.Width = Math.Max(1, _imageSize.Width * _zoom);
        PreviewImage.Height = Math.Max(1, _imageSize.Height * _zoom);
        ZoomLabel.Text = $"{_zoom:P0}";
    }
}
