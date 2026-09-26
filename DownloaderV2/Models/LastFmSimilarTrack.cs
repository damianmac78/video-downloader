namespace DownloaderV2.Models;

public sealed record LastFmSimilarTrack(
    string Title,
    string Artist,
    double? MatchScore,
    string? LastFmUrl);

