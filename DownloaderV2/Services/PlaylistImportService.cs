using DownloaderV2.Models;

namespace DownloaderV2.Services;

public sealed class PlaylistImportService(YtDlpService ytDlp)
{
    public bool IsPlaylistUrl(string url) => UrlCleaner.IsPlaylistUrl(url);

    public Task<PlaylistImport> AnalyzeAsync(string url, CancellationToken cancellationToken = default) =>
        ytDlp.AnalyzePlaylistAsync(UrlCleaner.NormalizePlaylist(url), cancellationToken);
}
