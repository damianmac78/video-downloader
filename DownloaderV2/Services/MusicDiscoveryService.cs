using DownloaderV2.Models;

namespace DownloaderV2.Services;

public interface IMusicDiscoveryService
{
    Task<IReadOnlyList<DiscoveryTrack>> FindSimilarAsync(Track sourceTrack, CancellationToken cancellationToken = default);
}

public sealed class MusicDiscoveryService(ILastFmService lastFm, YtDlpService ytDlp, MusicLibraryService library) : IMusicDiscoveryService
{
    private const int RecommendationLimit = 15;
    private const int SearchCandidateLimit = 5;
    private const int MaximumParallelResolutions = 3;

    public async Task<IReadOnlyList<DiscoveryTrack>> FindSimilarAsync(Track sourceTrack, CancellationToken cancellationToken = default)
    {
        var (sourceArtist, sourceTitle) = MusicMetadata.Clean(sourceTrack);
        if (string.IsNullOrWhiteSpace(sourceArtist) || string.IsNullOrWhiteSpace(sourceTitle))
            throw new LastFmException(LastFmFailureKind.MalformedResponse, "This track does not have enough artist and title metadata for discovery.");

        var recommendations = await lastFm.GetSimilarTracksAsync(sourceArtist, sourceTitle, RecommendationLimit, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal) { MusicMetadata.Identity(sourceArtist, sourceTitle) };
        var unique = recommendations.Where(item => seen.Add(MusicMetadata.Identity(item.Artist, item.Title))).ToArray();

        using var gate = new SemaphoreSlim(MaximumParallelResolutions);
        var tasks = unique.Select((recommendation, index) => ResolveAsync(recommendation, index, gate, cancellationToken)).ToArray();
        var resolved = await Task.WhenAll(tasks);
        var results = new List<DiscoveryTrack>();
        var resolvedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in resolved.Where(item => item.Track is not null).OrderBy(item => item.Index).Select(item => item.Track!))
        {
            if ((!string.IsNullOrWhiteSpace(sourceTrack.SourceVideoId) && sourceTrack.SourceVideoId.Equals(result.VideoId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(sourceTrack.SourceUrl) && sourceTrack.SourceUrl.Equals(result.SourceUrl, StringComparison.OrdinalIgnoreCase)))
                continue;

            var sourceKey = !string.IsNullOrWhiteSpace(result.VideoId)
                ? $"id:{result.VideoId}"
                : !string.IsNullOrWhiteSpace(result.SourceUrl)
                    ? $"url:{result.SourceUrl}"
                    : $"track:{MusicMetadata.Identity(result.Artist, result.Title)}";
            if (resolvedSources.Add(sourceKey)) results.Add(result);
        }
        return results;
    }

    private async Task<(int Index, DiscoveryTrack? Track)> ResolveAsync(
        LastFmSimilarTrack recommendation,
        int index,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existingByMetadata = await library.FindExistingAsync(null, null, recommendation.Title, recommendation.Artist, cancellationToken);
            if (existingByMetadata is not null)
                return (index, CreateResult(recommendation, existingByMetadata, existingByMetadata.SourceUrl,
                    existingByMetadata.SourceVideoId, existingByMetadata.Duration, existingByMetadata.ArtworkPath));

            IReadOnlyList<VideoInfo> candidates;
            try
            {
                candidates = await ytDlp.SearchVideosAsync($"{recommendation.Artist} {recommendation.Title}", SearchCandidateLimit, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return (index, null); }

            var best = YouTubeMatchScorer.SelectBest(recommendation.Artist, recommendation.Title, candidates);
            if (best is null) return (index, null);
            var sourceUrl = best.SourceUrl ?? (!string.IsNullOrWhiteSpace(best.SourceVideoId)
                ? $"https://www.youtube.com/watch?v={Uri.EscapeDataString(best.SourceVideoId)}"
                : null);
            if (string.IsNullOrWhiteSpace(sourceUrl)) return (index, null);

            var existing = await library.FindExistingAsync(best.SourceVideoId, sourceUrl, recommendation.Title, recommendation.Artist, cancellationToken);
            return (index, CreateResult(recommendation, existing, sourceUrl, best.SourceVideoId, best.Duration, best.ThumbnailUrl));
        }
        finally { gate.Release(); }
    }

    private static DiscoveryTrack CreateResult(
        LastFmSimilarTrack recommendation,
        Track? existing,
        string? sourceUrl,
        string? videoId,
        TimeSpan? duration,
        string? thumbnailUrl) => new()
    {
        Title = recommendation.Title,
        Artist = recommendation.Artist,
        Duration = duration,
        ThumbnailUrl = thumbnailUrl,
        SourceUrl = sourceUrl ?? string.Empty,
        VideoId = videoId,
        LastFmSimilarity = recommendation.MatchScore,
        ExistingTrack = existing,
        Status = existing is null ? "Available to download" : "In Library"
    };
}
