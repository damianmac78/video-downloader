using System.Windows;
using System.Windows.Media;

namespace DownloaderV2.Visualizers;

public sealed class WaveformRenderer() : VisualizerRenderer("Waveform / Oscilloscope")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var samples = context.Audio.Samples;
        if (samples.Length < 2) return;
        var centre = size.Height / 2;
        var points = new Point[samples.Length];
        for (var index = 0; index < samples.Length; index++)
            points[index] = new Point(index * size.Width / (samples.Length - 1), centre - samples[index] * centre * 0.82);
        dc.DrawGeometry(null, Pen(Green, 2), Polyline(points));
        dc.DrawGeometry(null, Pen(FrozenBrush(55, 236, 146, 55), 7), Polyline(points));
    }
}

public sealed class MirroredWaveformRenderer() : VisualizerRenderer("Mirrored Waveform")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var samples = context.Audio.Samples;
        if (samples.Length < 2) return;
        var centre = size.Height / 2;
        var upper = new Point[samples.Length];
        var lower = new Point[samples.Length];
        for (var index = 0; index < samples.Length; index++)
        {
            var x = index * size.Width / (samples.Length - 1);
            var amplitude = Math.Abs(samples[index]) * centre * 0.88;
            upper[index] = new Point(x, centre - amplitude);
            lower[index] = new Point(x, centre + amplitude);
        }
        dc.DrawGeometry(null, Pen(Cyan, 2), Polyline(upper));
        dc.DrawGeometry(null, Pen(Pink, 2), Polyline(lower));
    }
}

public sealed class RadialWaveformRenderer() : VisualizerRenderer("Radial Waveform")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var samples = context.Audio.Samples;
        if (samples.Length < 2) return;
        var centre = new Point(size.Width / 2, size.Height / 2);
        var radius = Math.Min(size.Width, size.Height) * 0.25;
        var points = new Point[samples.Length + 1];
        for (var index = 0; index <= samples.Length; index++)
        {
            var source = index % samples.Length;
            var angle = source / (double)samples.Length * Math.PI * 2 - Math.PI / 2;
            points[index] = Polar(centre, radius + samples[source] * radius * 0.75, angle);
        }
        dc.DrawGeometry(null, Pen(Pink, 2), Polyline(points, true));
    }
}

public sealed class PsychedelicRibbonRenderer() : VisualizerRenderer("Psychedelic Ribbon Trails")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var samples = context.Audio.Samples;
        if (samples.Length < 2) return;
        var time = context.Elapsed.TotalSeconds;
        for (var trail = 0; trail < 7; trail++)
        {
            var points = new Point[96];
            for (var index = 0; index < points.Length; index++)
            {
                var x = index * size.Width / (points.Length - 1);
                var sample = samples[index * samples.Length / points.Length];
                var wave = Math.Sin(index * 0.18 + time * (1.2 + context.Audio.Mid * 3) + trail * 0.7);
                var y = size.Height / 2 + (sample * 0.65 + wave * 0.18) * size.Height * 0.45 + (trail - 3) * 4;
                points[index] = new Point(x, y);
            }
            var brush = FrozenBrush((byte)(80 + trail * 24), (byte)(230 - trail * 16), 255, (byte)(180 - trail * 16));
            dc.DrawGeometry(null, Pen(brush, 1.5 + context.Audio.Rms * 4), Polyline(points));
        }
    }
}
