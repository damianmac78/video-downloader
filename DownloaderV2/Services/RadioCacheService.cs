using DownloaderV2.Models;

namespace DownloaderV2.Services;

public sealed class RadioCacheService
{
    private readonly SettingsService _settings;
    private readonly MusicLibraryService _library;

    public RadioCacheService(SettingsService settings, MusicLibraryService library, string? rootPath = null)
    {
        _settings = settings;
        _library = library;
        RootPath = rootPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloaderV2", "RadioCache");
    }

    public string RootPath { get; }
    public string TracksPath => Path.Combine(RootPath, "Tracks");
    public string ArtworkPath => Path.Combine(RootPath, "Artwork");

    public void EnsureFolders()
    {
        Directory.CreateDirectory(TracksPath);
        Directory.CreateDirectory(ArtworkPath);
    }

    public async Task<Track> KeepAsync(Track temporary, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(temporary.FilePath)) throw new FileNotFoundException("The temporary radio track is no longer available.", temporary.FilePath);
        Directory.CreateDirectory(_library.TracksFolder);
        Directory.CreateDirectory(_library.ArtworkFolder);
        var audioDestination = UniquePath(_library.TracksFolder, Path.GetFileName(temporary.FilePath));
        // The player may currently have the cache file open. Copying keeps playback
        // uninterrupted; normal cache cleanup removes the temporary original later.
        File.Copy(temporary.FilePath, audioDestination);
        string? artworkDestination = null;
        if (!string.IsNullOrWhiteSpace(temporary.ArtworkPath) && File.Exists(temporary.ArtworkPath))
        {
            artworkDestination = UniquePath(_library.ArtworkFolder, Path.GetFileName(temporary.ArtworkPath));
            File.Copy(temporary.ArtworkPath, artworkDestination);
        }
        return await _library.AddOrUpdateTrackAsync(new Track
        {
            Title = temporary.Title,
            Artist = temporary.Artist,
            Album = temporary.Album,
            FilePath = audioDestination,
            ArtworkPath = artworkDestination,
            DurationSeconds = temporary.DurationSeconds,
            SourceUrl = temporary.SourceUrl,
            SourceVideoId = temporary.SourceVideoId,
            DateAdded = DateTime.UtcNow
        }, cancellationToken);
    }

    public void Cleanup(IEnumerable<string> protectedPaths)
    {
        try
        {
            EnsureFolders();
            var protectedSet = protectedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path)).Where(file => !protectedSet.Contains(file.FullName)).OrderBy(file => file.LastWriteTimeUtc).ToList();
            var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(_settings.GetRadioCacheMaxAgeDays(), 1, 90));
            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff).ToArray()) TryDelete(file, files);
            var maximumBytes = Math.Clamp(_settings.GetRadioCacheMaxSizeGb(), 0.25, 50) * 1024 * 1024 * 1024;
            var total = Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories).Select(path => new FileInfo(path).Length).Sum();
            foreach (var file in files.ToArray())
            {
                if (total <= maximumBytes) break;
                if (!file.Exists) continue;
                var length = file.Length;
                TryDelete(file, files);
                if (!file.Exists) total -= length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(FileInfo file, ICollection<FileInfo> files)
    {
        try { file.Delete(); files.Remove(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string UniquePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;
        return Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
    }
}
