using System.Windows;
using System.Windows.Media;

namespace DownloaderV2.Visualizers;

public sealed class KaleidoscopeRenderer() : VisualizerRenderer("Kaleidoscope / Symmetry")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var samples = context.Audio.Samples;
        if (samples.Length == 0) return;
        var centre = new Point(size.Width / 2, size.Height / 2);
        var radius = Math.Min(size.Width, size.Height) * 0.42;
        const int segments = 8;
        for (var segment = 0; segment < segments; segment++)
        {
            var points = new Point[48];
            for (var i = 0; i < points.Length; i++)
            {
                var sample = Math.Abs(samples[i * samples.Length / points.Length]);
                var angle = segment * Math.PI * 2 / segments + (i / 47d - 0.5) * Math.PI * 2 / segments;
                points[i] = Polar(centre, radius * (0.18 + i / 58d) + sample * radius * 0.35, angle + context.Elapsed.TotalSeconds * 0.12);
            }
            dc.DrawGeometry(null, Pen(segment % 2 == 0 ? Pink : Cyan, 1.7), Polyline(points));
        }
    }
}

public sealed class ReactiveGridRenderer() : VisualizerRenderer("Audio Reactive Grid / Mesh")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var spectrum = context.Audio.Spectrum;
        if (spectrum.Length == 0) return;
        const int columns = 18;
        const int rows = 11;
        var points = new Point[rows, columns];
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var value = spectrum[column * spectrum.Length / columns];
            var x = column * size.Width / (columns - 1d);
            var baseY = row * size.Height / (rows - 1d);
            var perspective = 0.25 + row / (double)rows;
            var y = baseY - value * size.Height * 0.18 * perspective * Math.Sin(column * 0.65 + row * 0.42 + context.Elapsed.TotalSeconds);
            points[row, column] = new Point(x, y);
        }
        var pen = Pen(FrozenBrush(55, 236, 146, 145), 1);
        for (var row = 0; row < rows; row++) dc.DrawGeometry(null, pen, Polyline(Enumerable.Range(0, columns).Select(c => points[row, c])));
        for (var column = 0; column < columns; column++) dc.DrawGeometry(null, pen, Polyline(Enumerable.Range(0, rows).Select(r => points[r, column])));
    }
}

public sealed class PlasmaRenderer() : VisualizerRenderer("Plasma Audio Field")
{
    private static readonly Brush[] Palette = Enumerable.Range(0, 32).Select(i =>
    {
        var energy = i / 31d;
        return FrozenBrush((byte)(35 + energy * 220), (byte)(25 + energy * 85), (byte)(95 + (1 - energy) * 150), (byte)(80 + energy * 175));
    }).ToArray();
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        const int columns = 30;
        const int rows = 18;
        var width = size.Width / columns;
        var height = size.Height / rows;
        var time = context.Elapsed.TotalSeconds * (0.45 + context.Audio.Mid * 1.8);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
        {
            var wave = (Math.Sin(x * 0.31 + time) + Math.Sin(y * 0.43 - time * 1.2) + Math.Sin((x + y) * 0.22 + time * 0.7)) / 3;
            var energy = Math.Clamp((wave + 1) * 0.35 + context.Audio.Bass * 0.35 + context.Audio.Treble * ((x + y) % 5 == 0 ? 0.25 : 0), 0, 1);
            var brush = Palette[(int)Math.Round(energy * (Palette.Length - 1))];
            dc.DrawRectangle(brush, null, new Rect(x * width, y * height, width + 0.5, height + 0.5));
        }
    }
}
