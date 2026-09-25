using System.Windows;
using System.Windows.Media;

namespace DownloaderV2.Visualizers;

public abstract class SpectrumHistoryRenderer(string name, int capacity) : VisualizerRenderer(name)
{
    protected readonly Queue<float[]> History = new();
    private long _sequence;

    public override void Reset()
    {
        History.Clear();
        _sequence = 0;
    }

    public override void Update(in VisualizerRenderContext context)
    {
        if (context.Audio.Sequence == 0 || context.Audio.Sequence == _sequence) return;
        _sequence = context.Audio.Sequence;
        History.Enqueue((float[])context.Audio.Spectrum.Clone());
        while (History.Count > capacity) History.Dequeue();
    }
}

public sealed class FrequencyWaterfallRenderer() : SpectrumHistoryRenderer("Frequency Waterfall", 34)
{
    private static readonly Pen[] RowPens = Enumerable.Range(0, 34).Select(r => Pen(FrozenBrush(50, (byte)(105 + r * 4), 220, (byte)(70 + r * 5)), 1.2)).ToArray();
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var rows = History.ToArray();
        for (var r = 0; r < rows.Length; r++)
        {
            var spectrum = rows[r];
            var y = size.Height - (rows.Length - r) * size.Height / 38d;
            var points = new Point[Math.Min(48, spectrum.Length)];
            for (var i = 0; i < points.Length; i++)
                points[i] = new Point(i * size.Width / Math.Max(1, points.Length - 1), y - spectrum[i] * size.Height * 0.18);
            dc.DrawGeometry(null, RowPens[Math.Min(r, RowPens.Length - 1)], Polyline(points));
        }
    }
}

public sealed class SpectrogramRenderer() : SpectrumHistoryRenderer("Scrolling Spectrogram", 64)
{
    private static readonly Brush[] Palette = Enumerable.Range(0, 32).Select(i =>
    {
        var value = i / 31d;
        return FrozenBrush((byte)(25 + value * 230), (byte)(30 + value * 120), (byte)(80 + (1 - value) * 170), (byte)(80 + value * 175));
    }).ToArray();
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var columns = History.ToArray();
        if (columns.Length == 0) return;
        var cellWidth = size.Width / 64d;
        var bands = Math.Min(48, columns[0].Length);
        var cellHeight = size.Height / bands;
        for (var x = 0; x < columns.Length; x++)
        for (var y = 0; y < bands; y++)
        {
            var value = Math.Clamp(columns[x][y], 0, 1);
            var brush = Palette[(int)Math.Round(value * (Palette.Length - 1))];
            dc.DrawRectangle(brush, null, new Rect(size.Width - (columns.Length - x) * cellWidth, size.Height - (y + 1) * cellHeight, cellWidth + 0.5, cellHeight + 0.5));
        }
    }
}

public sealed class FrequencyTrailRenderer() : SpectrumHistoryRenderer("Frequency Trail / Persistence", 12)
{
    private static readonly Pen[] TrailPens = Enumerable.Range(0, 12).Select(t => Pen(FrozenBrush(55, 236, 146, (byte)(30 + 190d * (t + 1) / 12)), 1.2 + t / 8d)).ToArray();
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var trails = History.ToArray();
        for (var t = 0; t < trails.Length; t++)
        {
            var spectrum = trails[t];
            var count = Math.Min(56, spectrum.Length);
            var points = new Point[count];
            for (var i = 0; i < count; i++)
                points[i] = new Point(i * size.Width / Math.Max(1, count - 1), size.Height * 0.85 - spectrum[i] * size.Height * 0.72 - (trails.Length - t) * 2);
            dc.DrawGeometry(null, TrailPens[Math.Min(t, TrailPens.Length - 1)], Polyline(points));
        }
    }
}
