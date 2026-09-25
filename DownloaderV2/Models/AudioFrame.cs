namespace DownloaderV2.Models;

public sealed record AudioFrame(
    long Sequence,
    float[] Samples,
    float[] Spectrum,
    float Bass,
    float LowMid,
    float Mid,
    float HighMid,
    float Treble,
    float Rms,
    float Peak,
    float LeftLevel,
    float RightLevel)
{
    public static AudioFrame Empty { get; } = new(0, [], [], 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public bool HasAudio => Sequence > 0 && Samples.Length > 0;
}
