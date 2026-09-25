namespace DownloaderV2.Visualizers;

public static class VisualizerCatalog
{
    public const string DefaultName = "Classic Spectrum Bars";

    public static IReadOnlyList<IVisualizerRenderer> CreateRenderers() =>
    [
        new ClassicSpectrumRenderer(),
        new MirroredSpectrumRenderer(),
        new PeakSpectrumRenderer(),
        new WaveformRenderer(),
        new MirroredWaveformRenderer(),
        new RadialSpectrumRenderer(),
        new CircularSpectrumRenderer(),
        new AlbumArtSpectrumRenderer(),
        new RadialWaveformRenderer(),
        new ParticleFieldRenderer(),
        new BassPulseParticlesRenderer(),
        new StarfieldRenderer(),
        new FrequencyWaterfallRenderer(),
        new SpectrogramRenderer(),
        new FrequencyTrailRenderer(),
        new SpiralSpectrumRenderer(),
        new RotatingRingRenderer(),
        new KaleidoscopeRenderer(),
        new PsychedelicRibbonRenderer(),
        new ReactiveGridRenderer(),
        new EqualizerBlocksRenderer(),
        new VuMeterRenderer(),
        new StereoMeterRenderer(),
        new PlasmaRenderer(),
        new AlbumArtPulseRenderer()
    ];

    public static IReadOnlyList<string> Names { get; } = CreateRenderers().Select(renderer => renderer.Name).ToArray();
}
