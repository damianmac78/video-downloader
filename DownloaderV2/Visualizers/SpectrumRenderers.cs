using System.Windows;
using System.Windows.Media;

namespace DownloaderV2.Visualizers;

public sealed class ClassicSpectrumRenderer() : VisualizerRenderer("Classic Spectrum Bars")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var values = context.Audio.Spectrum;
        if (values.Length == 0) return;
        var count = Math.Min(48, values.Length);
        var gap = 2d;
        var width = Math.Max(1, (size.Width - gap * (count + 1)) / count);
        for (var index = 0; index < count; index++)
        {
            var height = Math.Max(1, values[index] * (size.Height - 8));
            dc.DrawRoundedRectangle(Green, null, new Rect(gap + index * (width + gap), size.Height - height - 3, width, height), 1, 1);
        }
    }
}

public sealed class MirroredSpectrumRenderer() : VisualizerRenderer("Mirrored Spectrum")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var values = context.Audio.Spectrum;
        if (values.Length == 0) return;
        var count = Math.Min(40, values.Length);
        var width = size.Width / count;
        var centre = size.Height / 2;
        for (var index = 0; index < count; index++)
        {
            var height = values[index] * centre * 0.92;
            var brush = index % 2 == 0 ? Cyan : Green;
            dc.DrawRectangle(brush, null, new Rect(index * width + 1, centre - height, Math.Max(1, width - 2), height * 2));
        }
        dc.DrawLine(Pen(FrozenBrush(70, 245, 190, 110)), new Point(0, centre), new Point(size.Width, centre));
    }
}

public sealed class PeakSpectrumRenderer() : VisualizerRenderer("Peak Meter Spectrum")
{
    private float[] _peaks = [];

    public override void Reset() => _peaks = [];

    public override void Update(in VisualizerRenderContext context)
    {
        var values = context.Audio.Spectrum;
        if (_peaks.Length != values.Length) _peaks = new float[values.Length];
        for (var index = 0; index < values.Length; index++)
            _peaks[index] = Math.Max(values[index], _peaks[index] - 0.012f);
    }

    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var values = context.Audio.Spectrum;
        if (values.Length == 0) return;
        var count = Math.Min(48, values.Length);
        var width = size.Width / count;
        for (var index = 0; index < count; index++)
        {
            var height = values[index] * (size.Height - 10);
            dc.DrawRectangle(Cyan, null, new Rect(index * width + 1, size.Height - height, Math.Max(1, width - 2), height));
            var heldPeak = index < _peaks.Length ? _peaks[index] : values[index];
            var peakY = size.Height - heldPeak * (size.Height - 10);
            dc.DrawRectangle(Pink, null, new Rect(index * width + 1, peakY, Math.Max(1, width - 2), 2));
        }
    }
}

public sealed class EqualizerBlocksRenderer() : VisualizerRenderer("Equaliser Blocks")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var values = context.Audio.Spectrum;
        if (values.Length == 0) return;
        const int columns = 32;
        const int rows = 16;
        var cellWidth = size.Width / columns;
        var cellHeight = size.Height / rows;
        for (var column = 0; column < columns; column++)
        {
            var active = (int)(values[column * values.Length / columns] * rows);
            for (var row = 0; row < active; row++)
            {
                var brush = row > 12 ? Pink : row > 8 ? Amber : Green;
                dc.DrawRectangle(brush, null, new Rect(column * cellWidth + 1, size.Height - (row + 1) * cellHeight + 1, Math.Max(1, cellWidth - 2), Math.Max(1, cellHeight - 2)));
            }
        }
    }
}

public sealed class VuMeterRenderer() : VisualizerRenderer("VU Meter")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var centre = new Point(size.Width / 2, size.Height * 0.88);
        var radius = Math.Min(size.Width * 0.42, size.Height * 0.75);
        for (var index = 0; index <= 20; index++)
        {
            var angle = Math.PI * (1.15 + index / 20d * 0.7);
            var inner = Polar(centre, radius * 0.83, angle);
            var outer = Polar(centre, radius, angle);
            dc.DrawLine(Pen(index > 16 ? Pink : Green, index % 5 == 0 ? 3 : 1), inner, outer);
        }
        var needleAngle = Math.PI * (1.15 + Math.Clamp(context.Audio.Rms * 2.3, 0, 1) * 0.7);
        dc.DrawLine(Pen(Amber, 3), centre, Polar(centre, radius * 0.9, needleAngle));
        dc.DrawEllipse(Amber, null, centre, 5, 5);
    }
}

public sealed class StereoMeterRenderer() : VisualizerRenderer("Dual-Channel Stereo Meter")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        DrawChannel(dc, new Rect(size.Width * 0.12, size.Height * 0.15, size.Width * 0.28, size.Height * 0.72), context.Audio.LeftLevel, "L");
        DrawChannel(dc, new Rect(size.Width * 0.60, size.Height * 0.15, size.Width * 0.28, size.Height * 0.72), context.Audio.RightLevel, "R");
    }

    private static void DrawChannel(DrawingContext dc, Rect rect, float level, string label)
    {
        dc.DrawRoundedRectangle(Dim, Pen(FrozenBrush(70, 90, 98)), rect, 5, 5);
        var fill = new Rect(rect.X + 5, rect.Bottom - 5 - (rect.Height - 10) * Math.Clamp(level * 2.2, 0, 1), rect.Width - 10, (rect.Height - 10) * Math.Clamp(level * 2.2, 0, 1));
        dc.DrawRectangle(level > 0.7 ? Pink : Green, null, fill);
        var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 16, Brushes.White, 1);
        dc.DrawText(text, new Point(rect.X + rect.Width / 2 - text.Width / 2, rect.Bottom + 4));
    }
}
