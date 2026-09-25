using System.Windows;
using System.Windows.Media;

namespace DownloaderV2.Controls;

public sealed class VisualizerControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(float[]), typeof(VisualizerControl),
        new FrameworkPropertyMetadata(Array.Empty<float>(), FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    private float[] _target = [];
    private float[] _display = [];

    public VisualizerControl()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    public float[] Values
    {
        get => (float[])GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    private static void OnValuesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (VisualizerControl)dependencyObject;
        control._target = e.NewValue as float[] ?? [];
        if (control._display.Length != control._target.Length) control._display = new float[control._target.Length];
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var moving = false;
        for (var index = 0; index < _display.Length; index++)
        {
            var target = index < _target.Length ? _target[index] : 0;
            var factor = target > _display[index] ? 0.42f : 0.12f;
            var next = _display[index] + (target - _display[index]) * factor;
            moving |= Math.Abs(next - _display[index]) > 0.001f;
            _display[index] = next;
        }
        if (moving || _display.Any(value => value > 0.002f)) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(14, 18, 21)), null,
            new Rect(0, 0, ActualWidth, ActualHeight), 4, 4);

        if (_display.Length == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var visibleBars = Math.Min(48, _display.Length);
        var gap = 2d;
        var width = Math.Max(1, (ActualWidth - gap * (visibleBars + 1)) / visibleBars);
        var accent = new SolidColorBrush(Color.FromRgb(55, 236, 146));
        accent.Freeze();

        for (var bar = 0; bar < visibleBars; bar++)
        {
            var sourceIndex = (int)(bar / (double)visibleBars * _display.Length);
            var height = Math.Max(1, _display[sourceIndex] * (ActualHeight - 8));
            var rectangle = new Rect(gap + bar * (width + gap), ActualHeight - 4 - height, width, height);
            drawingContext.DrawRoundedRectangle(accent, null, rectangle, 1, 1);
        }
    }
}
