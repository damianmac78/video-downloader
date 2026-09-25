using System.Windows;
using System.Windows.Media;

namespace DownloaderV2.Visualizers;

public sealed class ParticleFieldRenderer() : VisualizerRenderer("Audio Reactive Particle Field")
{
    private readonly Particle[] _particles = CreateParticles(96);
    private double _phase;

    public override void Reset() => _phase = 0;
    public override void Update(in VisualizerRenderContext context) => _phase += 0.012 + context.Audio.Mid * 0.055;

    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var centre = new Point(size.Width / 2, size.Height / 2);
        foreach (var particle in _particles)
        {
            var angle = particle.Angle + _phase * particle.Direction;
            var radius = particle.Radius * Math.Min(size.Width, size.Height) * (0.36 + context.Audio.Bass * 0.12);
            var point = Polar(centre, radius, angle);
            var sparkle = 1.2 + particle.Size * 2.2 + context.Audio.Treble * 4;
            dc.DrawEllipse(particle.Direction > 0 ? Cyan : Purple, null, point, sparkle, sparkle);
        }
    }

    private static Particle[] CreateParticles(int count) => Enumerable.Range(0, count)
        .Select(i => new Particle(i * 2.399963, 0.08 + (i % 23) / 25d, 0.35 + (i % 7) / 7d, i % 2 == 0 ? 1 : -1))
        .ToArray();

    private readonly record struct Particle(double Angle, double Radius, double Size, int Direction);
}

public sealed class BassPulseParticlesRenderer() : VisualizerRenderer("Bass Pulse Particles")
{
    private readonly double[] _angles = Enumerable.Range(0, 72).Select(i => i * Math.PI * 2 / 72).ToArray();

    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var centre = new Point(size.Width / 2, size.Height / 2);
        var min = Math.Min(size.Width, size.Height);
        var time = context.Elapsed.TotalSeconds;
        for (var i = 0; i < _angles.Length; i++)
        {
            var frequency = context.Audio.Spectrum.Length == 0 ? 0 : context.Audio.Spectrum[i % context.Audio.Spectrum.Length];
            var orbit = (time * (0.12 + context.Audio.Bass * 0.8) + i / (double)_angles.Length) % 1;
            var radius = min * (0.07 + orbit * 0.45) * (1 + context.Audio.Bass * 0.2);
            var point = Polar(centre, radius, _angles[i] + time * 0.25);
            var dot = 1.5 + frequency * 5 + context.Audio.Peak * 2;
            dc.DrawEllipse(i % 3 == 0 ? Pink : Amber, null, point, dot, dot);
        }
        dc.DrawEllipse(null, Pen(Pink, 2 + context.Audio.Bass * 5), centre, min * (0.08 + context.Audio.Bass * 0.08), min * (0.08 + context.Audio.Bass * 0.08));
    }
}

public sealed class StarfieldRenderer() : VisualizerRenderer("Starfield / Tunnel")
{
    private static readonly Brush DistantStar = FrozenBrush(120, 150, 170);
    private readonly Star[] _stars = Enumerable.Range(0, 120)
        .Select(i => new Star(Math.Sin(i * 47.13), Math.Cos(i * 91.71), (i % 40 + 1) / 40d))
        .ToArray();
    private double _travel;

    public override void Reset() => _travel = 0;
    public override void Update(in VisualizerRenderContext context) => _travel = (_travel + 0.004 + context.Audio.Bass * 0.03) % 1;

    public override void Render(DrawingContext dc, Size size, in VisualizerRenderContext context)
    {
        var centre = new Point(size.Width / 2, size.Height / 2);
        foreach (var star in _stars)
        {
            var depth = (star.Depth + _travel) % 1;
            var scale = 0.08 + depth * depth;
            var point = new Point(centre.X + star.X * size.Width * scale, centre.Y + star.Y * size.Height * scale);
            var tail = new Point(centre.X + star.X * size.Width * Math.Max(0, scale - 0.025 - context.Audio.Bass * 0.03), centre.Y + star.Y * size.Height * Math.Max(0, scale - 0.025 - context.Audio.Bass * 0.03));
            dc.DrawLine(Pen(depth > 0.65 ? Cyan : DistantStar, 0.7 + depth * 2.4), tail, point);
        }
    }

    private readonly record struct Star(double X, double Y, double Depth);
}
