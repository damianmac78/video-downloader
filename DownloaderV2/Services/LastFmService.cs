using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DownloaderV2.Models;

namespace DownloaderV2.Services;

public interface ILastFmService
{
    bool IsConfigured { get; }

    Task<IReadOnlyList<LastFmSimilarTrack>> GetSimilarTracksAsync(
        string artist,
        string title,
        int limit,
        CancellationToken cancellationToken = default);

    Task TestConnectionAsync(CancellationToken cancellationToken = default);
}

public enum LastFmFailureKind
{
    NotConfigured,
    InvalidApiKey,
    RateLimited,
    Network,
    MalformedResponse
}

public sealed class LastFmException(LastFmFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public LastFmFailureKind Kind { get; } = kind;
}

public sealed class LastFmService : ILastFmService
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public LastFmService(SettingsService settings, HttpClient? httpClient = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.GetLastFmApiKey());

    public async Task<IReadOnlyList<LastFmSimilarTrack>> GetSimilarTracksAsync(
        string artist,
        string title,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.GetLastFmApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new LastFmException(LastFmFailureKind.NotConfigured, "Add a Last.fm API key in Settings to use Discover Similar.");

        var safeLimit = Math.Clamp(limit, 1, 50);
        var cacheKey = $"{ApiKeyFingerprint(apiKey)}|{Normalize(artist)}|{Normalize(title)}|{safeLimit}";
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAt < CacheDuration)
            return cached.Tracks;

        var uri = BuildUri(apiKey, artist, title, safeLimit);
        using var response = await SendAsync(uri, cancellationToken);
        var tracks = await ParseResponseAsync(response, cancellationToken);
        _cache[cacheKey] = new CacheEntry(DateTimeOffset.UtcNow, tracks);
        return tracks;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        _ = await GetSimilarTracksAsync("Cher", "Believe", 1, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                response.Dispose();
                throw new LastFmException(LastFmFailureKind.RateLimited, "Last.fm is temporarily rate limiting requests. Please try again later.");
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (LastFmException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new LastFmException(LastFmFailureKind.Network, "Last.fm could not be reached. Check your connection and try again.", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new LastFmException(LastFmFailureKind.Network, "The Last.fm request timed out.", ex);
        }
    }

    private static async Task<IReadOnlyList<LastFmSimilarTrack>> ParseResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (TryGetError(root, out var errorCode, out var errorMessage))
            {
                // Last.fm uses error 6 when the artist/track cannot be resolved.
                // That is a valid empty recommendation set, not an application failure.
                if (errorCode == 6) return [];

                var kind = errorCode is 10 or 26
                    ? LastFmFailureKind.InvalidApiKey
                    : errorCode == 29
                        ? LastFmFailureKind.RateLimited
                        : LastFmFailureKind.MalformedResponse;
                var friendly = kind switch
                {
                    LastFmFailureKind.InvalidApiKey => "The Last.fm API key is invalid.",
                    LastFmFailureKind.RateLimited => "Last.fm is temporarily rate limiting requests. Please try again later.",
                    _ => string.IsNullOrWhiteSpace(errorMessage) ? "Last.fm could not return recommendations for this track." : errorMessage
                };
                throw new LastFmException(kind, friendly);
            }

            if (!response.IsSuccessStatusCode)
                throw new LastFmException(LastFmFailureKind.Network, $"Last.fm returned HTTP {(int)response.StatusCode}.");

            if (!root.TryGetProperty("similartracks", out var similar) || similar.ValueKind != JsonValueKind.Object ||
                !similar.TryGetProperty("track", out var trackArray) || trackArray.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<LastFmSimilarTrack>();
            foreach (var item in trackArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var trackTitle = GetString(item, "name");
                if (!item.TryGetProperty("artist", out var artistObject) || artistObject.ValueKind != JsonValueKind.Object) continue;
                var trackArtist = GetString(artistObject, "name");
                if (string.IsNullOrWhiteSpace(trackTitle) || string.IsNullOrWhiteSpace(trackArtist)) continue;
                results.Add(new LastFmSimilarTrack(trackTitle, trackArtist, GetNullableDouble(item, "match"), GetString(item, "url")));
            }

            return results;
        }
        catch (OperationCanceledException) { throw; }
        catch (LastFmException) { throw; }
        catch (JsonException ex)
        {
            throw new LastFmException(LastFmFailureKind.MalformedResponse, "Last.fm returned an unreadable response.", ex);
        }
    }

    private static bool TryGetError(JsonElement root, out int code, out string? message)
    {
        code = 0;
        message = null;
        if (!root.TryGetProperty("error", out var error)) return false;
        if (error.ValueKind == JsonValueKind.Number) error.TryGetInt32(out code);
        else if (error.ValueKind == JsonValueKind.String) int.TryParse(error.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
        message = GetString(root, "message");
        return true;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? GetNullableDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static Uri BuildUri(string apiKey, string artist, string title, int limit)
    {
        var query = new Dictionary<string, string>
        {
            ["method"] = "track.getSimilar",
            ["artist"] = artist,
            ["track"] = title,
            ["api_key"] = apiKey,
            ["format"] = "json",
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["autocorrect"] = "1"
        };
        return new Uri("https://ws.audioscrobbler.com/2.0/?" + string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DownloaderV2/1.0");
        return client;
    }

    private static string Normalize(string value) => string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string ApiKeyFingerprint(string apiKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
    private sealed record CacheEntry(DateTimeOffset CreatedAt, IReadOnlyList<LastFmSimilarTrack> Tracks);
}
