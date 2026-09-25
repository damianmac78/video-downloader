using System.Windows;
using System.Windows.Media;
using DownloaderV2.Models;

namespace DownloaderV2.Visualizers;

public readonly record struct VisualizerRenderContext(
    AudioFrame Audio,
    TimeSpan Elapsed,
    ImageSource? Artwork);

public interface IVisualizerRenderer
{
    string Name { get; }
    void Reset();
    void Update(in VisualizerRenderContext context);
    void Render(DrawingContext drawingContext, Size renderSize, in VisualizerRenderContext context);
}

public abstract class VisualizerRenderer(string name) : IVisualizerRenderer
{
    protected static readonly Brush Green = FrozenBrush(55, 236, 146);
    protected static readonly Brush Cyan = FrozenBrush(54, 206, 255);
    protected static readonly Brush Pink = FrozenBrush(255, 68, 180);
    protected static readonly Brush Purple = FrozenBrush(137, 92, 255);
    protected static readonly Brush Amber = FrozenBrush(255, 184, 77);
    protected static readonly Brush Dim = FrozenBrush(28, 42, 48);

    public string Name { get; } = name;
    public virtual void Reset() { }
    public virtual void Update(in VisualizerRenderContext context) { }
    public abstract void Render(DrawingContext drawingContext, Size renderSize, in VisualizerRenderContext context);

    protected static Point Polar(Point centre, double radius, double angle) =>
        new(centre.X + Math.Cos(angle) * radius, centre.Y + Math.Sin(angle) * radius);

    protected static Pen Pen(Brush brush, double thickness = 1.5)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    protected static void DrawArtwork(DrawingContext dc, ImageSource? artwork, Rect rect, double opacity = 1)
    {
        if (artwork is null) return;
        dc.PushOpacity(opacity);
        dc.DrawImage(artwork, rect);
        dc.Pop();
    }

    protected static StreamGeometry Polyline(IEnumerable<Point> points, bool closed = false)
    {
        var list = points.ToList();
        var geometry = new StreamGeometry();
        if (list.Count == 0) return geometry;
        using (var context = geometry.Open())
        {
            context.BeginFigure(list[0], false, closed);
            if (list.Count > 1) context.PolyLineTo(list.Skip(1).ToList(), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    protected static Brush FrozenBrush(byte red, byte green, byte blue, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}
