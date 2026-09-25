using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DownloaderV2.Models;
using DownloaderV2.Visualizers;

namespace DownloaderV2.Controls;

public sealed class VisualizerControl : FrameworkElement
{
    private static readonly Brush Background = CreateBrush(14, 18, 21);
    private readonly Dictionary<string, IVisualizerRenderer> _renderers = VisualizerCatalog.CreateRenderers().ToDictionary(item => item.Name);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastRender;
    private IVisualizerRenderer? _renderer;
    private ImageSource? _artwork;
    private string? _loadedArtworkPath;
    private bool _isSubscribed;
    private bool _isLoaded;

    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(nameof(Frame), typeof(AudioFrame), typeof(VisualizerControl), new FrameworkPropertyMetadata(AudioFrame.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnFrameChanged));
    public static readonly DependencyProperty RendererNameProperty = DependencyProperty.Register(nameof(RendererName), typeof(string), typeof(VisualizerControl), new FrameworkPropertyMetadata(VisualizerCatalog.DefaultName, FrameworkPropertyMetadataOptions.AffectsRender, OnRendererChanged));
    public static readonly DependencyProperty ArtworkPathProperty = DependencyProperty.Register(nameof(ArtworkPath), typeof(string), typeof(VisualizerControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnArtworkChanged));

    public VisualizerControl()
    {
        _renderer = _renderers[VisualizerCatalog.DefaultName];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public AudioFrame Frame { get => (AudioFrame?)GetValue(FrameProperty) ?? AudioFrame.Empty; set => SetValue(FrameProperty, value); }
    public string RendererName { get => (string)GetValue(RendererNameProperty); set => SetValue(RendererNameProperty, value); }
    public string? ArtworkPath { get => (string?)GetValue(ArtworkPathProperty); set => SetValue(ArtworkPathProperty, value); }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (Frame.HasAudio) SubscribeRendering();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        UnsubscribeRendering();
    }

    private static void OnFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VisualizerControl)d;
        var frame = e.NewValue as AudioFrame ?? AudioFrame.Empty;
        if (control._isLoaded && frame.HasAudio) control.SubscribeRendering();
        else if (!frame.HasAudio)
        {
            control.UnsubscribeRendering();
            foreach (var renderer in control._renderers.Values) renderer.Reset();
        }
    }

    private void SubscribeRendering()
    {
        if (_isSubscribed) return;
        CompositionTarget.Rendering += OnRendering;
        _isSubscribed = true;
    }

    private void UnsubscribeRendering()
    {
        if (!_isSubscribed) return;
        CompositionTarget.Rendering -= OnRendering;
        _isSubscribed = false;
    }

    private static void OnRendererChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (VisualizerControl)d;
        var name = e.NewValue as string;
        control._renderer = name is not null && control._renderers.TryGetValue(name, out var renderer) ? renderer : control._renderers[VisualizerCatalog.DefaultName];
        control._renderer.Reset();
    }

    private static void OnArtworkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((VisualizerControl)d).LoadArtwork(e.NewValue as string);

    private void LoadArtwork(string? path)
    {
        if (string.Equals(path, _loadedArtworkPath, StringComparison.OrdinalIgnoreCase)) return;
        _loadedArtworkPath = path;
        _artwork = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 700;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            _artwork = image;
        }
        catch { _artwork = null; }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        if (now - _lastRender < TimeSpan.FromMilliseconds(16)) return;
        _lastRender = now;
        var context = new VisualizerRenderContext(Frame ?? AudioFrame.Empty, now, _artwork);
        _renderer?.Update(context);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Background, null, new Rect(RenderSize));
        if (ActualWidth <= 0 || ActualHeight <= 0 || _renderer is null || !Frame.HasAudio) return;
        _renderer.Render(dc, RenderSize, new VisualizerRenderContext(Frame, _clock.Elapsed, _artwork));
    }

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
