using DownloaderV2.Models;

namespace DownloaderV2.Services;

public sealed class DownloadLibraryService(SettingsService settings, MusicLibraryService musicLibrary)
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".m4a", ".mp3", ".aac", ".opus", ".ogg", ".wav", ".flac" };

    public Task<IReadOnlyList<DownloadedMediaItem>> GetDownloadsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DownloadedMediaItem>>(() => Scan(cancellationToken), cancellationToken);

    private IReadOnlyList<DownloadedMediaItem> Scan(CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, DownloadedMediaItem>(StringComparer.OrdinalIgnoreCase);
        var folders = new[] { settings.GetDownloadFolder(), musicLibrary.TracksFolder }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder)) continue;
            try
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(path);
                    var mediaType = VideoExtensions.Contains(extension) ? "Video" : AudioExtensions.Contains(extension) ? "Audio" : null;
                    if (mediaType is null) continue;
                    try
                    {
                        var file = new FileInfo(path);
                        results[path] = new DownloadedMediaItem(
                            Path.GetFileNameWithoutExtension(path), mediaType, extension.TrimStart('.').ToUpperInvariant(),
                            path, file.DirectoryName ?? folder, file.LastWriteTimeUtc, file.Length);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return results.Values.OrderByDescending(item => item.Modified).ToArray();
    }
}
