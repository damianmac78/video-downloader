namespace DownloaderV2.Models;

public sealed record VideoInfo(
    string Title,
    string Uploader,
    TimeSpan? Duration,
    string? ThumbnailUrl,
    string? SourceVideoId = null,
    string? SourceUrl = null)
{
    public string DurationText => Duration?.ToString(@"hh\:mm\:ss") ?? "Unknown duration";
}
