namespace DownloaderV2.Models;

public sealed class Track
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string? Album { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string? ArtworkPath { get; init; }
    public double DurationSeconds { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourceVideoId { get; init; }
    public DateTime DateAdded { get; init; }

    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
    public string DurationText => Duration.ToString(Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
}
