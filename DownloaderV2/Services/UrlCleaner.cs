namespace DownloaderV2.Services;

public static class UrlCleaner
{
    public static string Clean(string url) => CleanSingleVideo(url);

    public static bool IsPlaylistUrl(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (IsYouTubeHost(uri.Host))
            return !string.IsNullOrWhiteSpace(ParseQuery(uri.Query).GetValueOrDefault("list")) ||
                   uri.AbsolutePath.Equals("/playlist", StringComparison.OrdinalIgnoreCase);
        return uri.AbsolutePath.Contains("playlist", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/sets/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/album/", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizePlaylist(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || !IsYouTubeHost(uri.Host)) return trimmed;
        var playlistId = ParseQuery(uri.Query).GetValueOrDefault("list");
        return string.IsNullOrWhiteSpace(playlistId)
            ? trimmed
            : $"https://www.youtube.com/playlist?list={Uri.EscapeDataString(playlistId)}";
    }

    public static string CleanSingleVideo(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || !IsYouTubeHost(uri.Host))
        {
            return trimmed;
        }

        var videoId = GetVideoId(uri);
        return string.IsNullOrWhiteSpace(videoId)
            ? trimmed
            : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
    }

    private static bool IsYouTubeHost(string host) =>
        host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtu.be", StringComparison.OrdinalIgnoreCase);

    private static string? GetVideoId(Uri uri)
    {
        if (uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        if (uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
        {
            return ParseQuery(uri.Query).GetValueOrDefault("v");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 &&
            (segments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
             segments[0].Equals("live", StringComparison.OrdinalIgnoreCase)))
        {
            return segments[1];
        }

        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1])
                : string.Empty;
        }
        return result;
    }
}
