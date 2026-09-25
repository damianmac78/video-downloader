using System.Windows;
using System.Windows.Media;

namespace DownloaderV2.Visualizers;

public sealed class RadialSpectrumRenderer() : VisualizerRenderer("Radial Spectrum")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var spectrum = context.Audio.Spectrum;
        if (spectrum.Length == 0) return;
        var centre = new Point(size.Width / 2, size.Height / 2);
        var inner = Math.Min(size.Width, size.Height) * 0.16;
        var range = Math.Min(size.Width, size.Height) * 0.27;
        for (var index = 0; index < spectrum.Length; index++)
        {
            var angle = index / (double)spectrum.Length * Math.PI * 2 - Math.PI / 2;
            dc.DrawLine(Pen(index % 2 == 0 ? Green : Cyan, 2), Polar(centre, inner, angle), Polar(centre, inner + spectrum[index] * range, angle));
        }
    }
}

public sealed class CircularSpectrumRenderer() : VisualizerRenderer("Circular Spectrum")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var spectrum = context.Audio.Spectrum;
        if (spectrum.Length == 0) return;
        var centre = new Point(size.Width / 2, size.Height / 2);
        var radius = Math.Min(size.Width, size.Height) * 0.24;
        var points = new Point[spectrum.Length + 1];
        for (var index = 0; index <= spectrum.Length; index++)
        {
            var source = index % spectrum.Length;
            var angle = source / (double)spectrum.Length * Math.PI * 2;
            points[index] = Polar(centre, radius + spectrum[source] * radius, angle);
        }
        dc.DrawGeometry(null, Pen(Cyan, 2), Polyline(points, true));
        dc.DrawEllipse(null, Pen(FrozenBrush(55, 236, 146, 80)), centre, radius, radius);
    }
}

public sealed class AlbumArtSpectrumRenderer() : VisualizerRenderer("Circular Spectrum + Album Art")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var centre = new Point(size.Width / 2, size.Height / 2);
        var artRadius = Math.Min(size.Width, size.Height) * (0.18 + context.Audio.Bass * 0.035);
        if (context.Artwork is not null)
        {
            dc.PushClip(new EllipseGeometry(centre, artRadius, artRadius));
            DrawArtwork(dc, context.Artwork, new Rect(centre.X - artRadius, centre.Y - artRadius, artRadius * 2, artRadius * 2));
            dc.Pop();
        }
        dc.DrawEllipse(null, Pen(Pink, 3), centre, artRadius + 3, artRadius + 3);
        var spectrum = context.Audio.Spectrum;
        for (var index = 0; index < spectrum.Length; index++)
        {
            var angle = index / (double)spectrum.Length * Math.PI * 2 - Math.PI / 2;
            var start = artRadius + 8;
            var end = start + spectrum[index] * Math.Min(size.Width, size.Height) * 0.2;
            dc.DrawLine(Pen(index % 3 == 0 ? Pink : Green, 2), Polar(centre, start, angle), Polar(centre, end, angle));
        }
    }
}

public sealed class SpiralSpectrumRenderer() : VisualizerRenderer("Spiral Spectrum")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var spectrum = context.Audio.Spectrum;
        if (spectrum.Length == 0) return;
        var centre = new Point(size.Width / 2, size.Height / 2);
        var maxRadius = Math.Min(size.Width, size.Height) * 0.43;
        var rotation = context.Elapsed.TotalSeconds * (0.25 + context.Audio.Bass);
        var points = new List<Point>(spectrum.Length * 3);
        for (var turn = 0; turn < 3; turn++)
        for (var index = 0; index < spectrum.Length; index++)
        {
            var progress = (turn * spectrum.Length + index) / (double)(spectrum.Length * 3);
            var angle = progress * Math.PI * 6 + rotation;
            var radius = 5 + progress * maxRadius + spectrum[index] * 18;
            points.Add(Polar(centre, radius, angle));
        }
        dc.DrawGeometry(null, Pen(Purple, 2), Polyline(points));
    }
}

public sealed class RotatingRingRenderer() : VisualizerRenderer("Rotating Ring Spectrum")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var spectrum = context.Audio.Spectrum;
        if (spectrum.Length == 0) return;
        var centre = new Point(size.Width / 2, size.Height / 2);
        var baseRadius = Math.Min(size.Width, size.Height) * 0.22;
        var rotation = context.Elapsed.TotalSeconds * (0.6 + context.Audio.Mid * 2);
        for (var ring = 0; ring < 3; ring++)
        {
            var points = new Point[spectrum.Length + 1];
            for (var index = 0; index <= spectrum.Length; index++)
            {
                var source = index % spectrum.Length;
                var angle = source / (double)spectrum.Length * Math.PI * 2 + rotation * (ring % 2 == 0 ? 1 : -1);
                var radius = baseRadius + ring * 22 + spectrum[source] * (18 + ring * 8);
                points[index] = Polar(centre, radius, angle);
            }
            dc.DrawGeometry(null, Pen(ring == 0 ? Green : ring == 1 ? Cyan : Pink, 1.8), Polyline(points, true));
        }
    }
}

public sealed class AlbumArtPulseRenderer() : VisualizerRenderer("Album-Art Reactive Pulse")
{
    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var centre = new Point(size.Width / 2, size.Height / 2);
        var baseRadius = Math.Min(size.Width, size.Height) * 0.25;
        for (var pulse = 4; pulse >= 1; pulse--)
        {
            var radius = baseRadius + pulse * 12 + context.Audio.Bass * pulse * 18;
            dc.DrawEllipse(null, Pen(FrozenBrush(55, 236, 146, (byte)(35 + pulse * 25)), 2), centre, radius, radius);
        }
        var artRadius = baseRadius * (0.82 + context.Audio.Rms * 0.18);
        if (context.Artwork is not null)
        {
            dc.PushClip(new EllipseGeometry(centre, artRadius, artRadius));
            DrawArtwork(dc, context.Artwork, new Rect(centre.X - artRadius, centre.Y - artRadius, artRadius * 2, artRadius * 2));
            dc.Pop();
        }
        else dc.DrawEllipse(Dim, Pen(Green, 2), centre, artRadius, artRadius);
    }
}
