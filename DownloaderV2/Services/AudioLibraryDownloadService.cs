using DownloaderV2.Models;

namespace DownloaderV2.Services;

public sealed class AudioLibraryDownloadService(
    YtDlpService ytDlp,
    MusicLibraryService library,
    ArtworkService artwork)
{
    private static readonly VideoFormatOption AudioFormat = VideoFormatOption.Defaults.First(option => option.AudioOnly);

    public async Task<AudioLibraryDownloadResult> DownloadAsync(
        string url,
        VideoInfo? knownInfo = null,
        AnalysisResult? knownAnalysis = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await library.FindExistingAsync(
            knownInfo?.SourceVideoId, knownInfo?.SourceUrl ?? url, knownInfo?.Title, knownInfo?.Uploader, cancellationToken);
        if (existing is not null) return new AudioLibraryDownloadResult(existing, true);

        var analysis = knownAnalysis is { IsSuccess: true } ? knownAnalysis : await ytDlp.AnalyzeAsync(url, cancellationToken);
        if (!analysis.IsSuccess || analysis.VideoInfo is null)
            throw new YtDlpException(analysis.DiagnosticMessage, analysis.RawStdErr);

        var video = analysis.VideoInfo;
        existing = await library.FindExistingAsync(video.SourceVideoId, video.SourceUrl ?? url, video.Title, video.Uploader, cancellationToken);
        if (existing is not null) return new AudioLibraryDownloadResult(existing, true);

        var started = DateTime.UtcNow.AddSeconds(-2);
        var result = await ytDlp.DownloadAsync(
            url, AudioFormat, library.TracksFolder, analysis.SuccessfulStrategy, progress, cancellationToken);
        var filePath = ResolveOutputFile(result.OutputFilePath, library.TracksFolder, started)
            ?? throw new YtDlpException("The audio downloaded, but its output file could not be located.");
        var artworkPath = await artwork.CacheAsync(video.SourceVideoId, video.ThumbnailUrl, cancellationToken);
        var track = await library.AddOrUpdateTrackAsync(new Track
        {
            Title = video.Title,
            Artist = video.Uploader,
            FilePath = filePath,
            ArtworkPath = artworkPath,
            DurationSeconds = video.Duration?.TotalSeconds ?? 0,
            SourceUrl = video.SourceUrl ?? url,
            SourceVideoId = video.SourceVideoId,
            DateAdded = DateTime.UtcNow
        }, cancellationToken);
        return new AudioLibraryDownloadResult(track, false);
    }

    private static string? ResolveOutputFile(string? reportedPath, string folder, DateTime started)
    {
        if (!string.IsNullOrWhiteSpace(reportedPath) && File.Exists(reportedPath)) return reportedPath;
        if (!Directory.Exists(folder)) return null;
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".m4a", ".mp3", ".aac", ".opus", ".ogg", ".webm", ".wav", ".flac" };
        return Directory.EnumerateFiles(folder)
            .Where(path => extensions.Contains(Path.GetExtension(path)) && File.GetLastWriteTimeUtc(path) >= started)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
