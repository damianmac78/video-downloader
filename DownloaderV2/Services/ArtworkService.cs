namespace DownloaderV2.Services;

using System.Net.Http;

public sealed class ArtworkService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _artworkFolder;

    public ArtworkService(string artworkFolder)
    {
        _artworkFolder = artworkFolder;
        Directory.CreateDirectory(_artworkFolder);
    }

    public async Task<string?> CacheAsync(string? videoId, string? artworkUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(artworkUrl)) return null;

        var safeId = string.Concat(videoId.Where(character => !Path.GetInvalidFileNameChars().Contains(character)));
        var destination = Path.Combine(_artworkFolder, $"{safeId}.jpg");
        if (File.Exists(destination)) return destination;

        try
        {
            using var response = await HttpClient.GetAsync(artworkUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);
            return destination;
        }
        catch (HttpRequestException)
        {
            DeletePartialFile(destination);
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DeletePartialFile(destination);
            return null;
        }
    }

    private static void DeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
