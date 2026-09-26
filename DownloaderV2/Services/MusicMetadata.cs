using System.Text.RegularExpressions;
using DownloaderV2.Models;

namespace DownloaderV2.Services;

public static partial class MusicMetadata
{
    public static (string Artist, string Title) Clean(Track track)
    {
        var artist = CleanArtist(track.Artist);
        var title = CleanTitle(track.Title);
        var separator = FindArtistTitleSeparator(title);
        if (separator.Index > 0)
        {
            var possibleArtist = title[..separator.Index].Trim();
            var possibleTitle = title[(separator.Index + separator.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(possibleArtist) && !string.IsNullOrWhiteSpace(possibleTitle))
            {
                // YouTube frequently reports the uploader/channel as Artist while
                // keeping the real artist in the conventional "Artist - Title"
                // video title. Prefer the metadata embedded in the track name.
                artist = possibleArtist;
                title = possibleTitle;
            }
        }

        return (artist, CleanTitle(title));
    }

    public static string Normalize(string value) =>
        WhitespaceRegex().Replace(NonAlphaNumericRegex().Replace(value.ToLowerInvariant(), " "), " ").Trim();

    public static string Identity(string artist, string title) => $"{Normalize(artist)}|{Normalize(title)}";

    private static string CleanArtist(string value) => TopicSuffixRegex().Replace(value ?? string.Empty, string.Empty).Trim();

    private static string CleanTitle(string value)
    {
        var result = value?.Trim() ?? string.Empty;
        string previous;
        do
        {
            previous = result;
            result = ObviousSuffixRegex().Replace(result, string.Empty).Trim();
        } while (!string.Equals(previous, result, StringComparison.Ordinal));
        return result;
    }

    private static (int Index, int Length) FindArtistTitleSeparator(string title)
    {
        var matches = new[] { " - ", " – ", " — " }
            .Select(separator => (Index: title.IndexOf(separator, StringComparison.Ordinal), separator.Length))
            .Where(match => match.Index > 0)
            .OrderBy(match => match.Index)
            .ToArray();
        return matches.Length == 0 ? (-1, 0) : matches[0];
    }

    [GeneratedRegex(@"\s*-\s*topic$", RegexOptions.IgnoreCase)]
    private static partial Regex TopicSuffixRegex();

    [GeneratedRegex(@"\s*(?:[\(\[]\s*(?:official\s+(?:music\s+)?video|official\s+audio|audio|video|lyrics?|lyric\s+video)\s*[\)\]]|(?:official\s+(?:music\s+)?video|official\s+audio|lyrics?))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ObviousSuffixRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public static class YouTubeMatchScorer
{
    private static readonly string[] PreferredTerms = ["official audio", "official video", "official music video", "topic"];
    private static readonly string[] AlternateTerms = ["live", "cover", "reaction", "karaoke", "slowed", "sped up", "nightcore", "remix"];

    public static double Score(string recommendedArtist, string recommendedTitle, VideoInfo candidate)
    {
        var targetTitle = MusicMetadata.Normalize(recommendedTitle);
        var targetArtist = MusicMetadata.Normalize(recommendedArtist);
        var candidateTitle = MusicMetadata.Normalize(candidate.Title);
        var candidateContext = MusicMetadata.Normalize($"{candidate.Title} {candidate.Uploader}");
        if (targetTitle.Length == 0 || candidateTitle.Length == 0) return 0;

        var score = TokenCoverage(targetTitle, candidateTitle) * 0.55;
        score += TokenCoverage(targetArtist, candidateContext) * 0.30;
        if (candidateTitle.Contains(targetTitle, StringComparison.Ordinal)) score += 0.15;
        if (PreferredTerms.Any(term => ContainsTerm(candidateContext, term))) score += 0.05;
        foreach (var term in AlternateTerms)
            if (ContainsTerm(candidateContext, term) && !ContainsTerm(targetTitle, term)) score -= 0.30;
        return Math.Clamp(score, 0, 1);
    }

    public static VideoInfo? SelectBest(string artist, string title, IEnumerable<VideoInfo> candidates, double threshold = 0.45) =>
        candidates.Select(candidate => (Candidate: candidate, Score: Score(artist, title, candidate)))
            .Where(item => item.Score >= threshold)
            .OrderByDescending(item => item.Score)
            .Select(item => item.Candidate)
            .FirstOrDefault();

    private static double TokenCoverage(string expected, string actual)
    {
        var tokens = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
        if (tokens.Length == 0) return 0;
        var actualTokens = actual.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return tokens.Count(actualTokens.Contains) / (double)tokens.Length;
    }

    private static bool ContainsTerm(string value, string term) =>
        $" {value} ".Contains($" {term} ", StringComparison.Ordinal);
}
