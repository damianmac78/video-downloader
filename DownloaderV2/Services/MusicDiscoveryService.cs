using DownloaderV2.Models;

namespace DownloaderV2.Services;

public interface IMusicDiscoveryService
{
    Task<IReadOnlyList<DiscoveryTrack>> FindSimilarAsync(Track sourceTrack, CancellationToken cancellationToken = default);
}

public sealed class MusicDiscoveryService(YtDlpService ytDlp, MusicLibraryService library) : IMusicDiscoveryService
{
    public async Task<IReadOnlyList<DiscoveryTrack>> FindSimilarAsync(Track sourceTrack, CancellationToken cancellationToken = default)
    {
        var artist = string.IsNullOrWhiteSpace(sourceTrack.Artist) ? sourceTrack.Title : sourceTrack.Artist;
        var searches = await Task.WhenAll(
            ytDlp.SearchVideosAsync($"{artist} similar songs", 12, cancellationToken),
            ytDlp.SearchVideosAsync($"{artist} {sourceTrack.Title}", 10, cancellationToken));

        var results = new List<DiscoveryTrack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var video in searches.SelectMany(item => item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = video.SourceVideoId ?? video.SourceUrl ?? $"{video.Uploader}|{video.Title}";
            if (!seen.Add(key) ||
                (!string.IsNullOrWhiteSpace(sourceTrack.SourceVideoId) && sourceTrack.SourceVideoId.Equals(video.SourceVideoId, StringComparison.OrdinalIgnoreCase)))
                continue;

            var existing = await library.FindExistingAsync(video.SourceVideoId, video.SourceUrl, video.Title, video.Uploader, cancellationToken);
            results.Add(new DiscoveryTrack
            {
                Title = video.Title,
                Artist = video.Uploader,
                Duration = video.Duration,
                ThumbnailUrl = video.ThumbnailUrl,
                SourceUrl = video.SourceUrl ?? $"https://www.youtube.com/watch?v={video.SourceVideoId}",
                VideoId = video.SourceVideoId,
                ExistingTrack = existing,
                Status = existing is null ? "Available to download" : "Already in Music"
            });
            if (results.Count >= 20) break;
        }
        return results;
    }
}
