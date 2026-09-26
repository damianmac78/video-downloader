using DownloaderV2.Models;
using System.Text.Json;
using NAudio.Wave;

namespace DownloaderV2.Services;

public sealed class RadioCacheService
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".m4a", ".mp3", ".aac", ".opus", ".ogg", ".webm", ".wav", ".flac", ".aiff", ".wma" };
    private readonly SettingsService _settings;
    private readonly MusicLibraryService _library;

    public RadioCacheService(SettingsService settings, MusicLibraryService library, string? rootPath = null)
    {
        _settings = settings;
        _library = library;
        RootPath = Path.GetFullPath(rootPath ?? settings.GetRadioCacheFolder());
    }

    public string RootPath { get; private set; }
    public string TracksPath => Path.Combine(RootPath, "Tracks");
    public string ArtworkPath => Path.Combine(RootPath, "Artwork");

    public void EnsureFolders()
    {
        Directory.CreateDirectory(TracksPath);
        Directory.CreateDirectory(ArtworkPath);
    }

    public void SetRootPath(string folder)
    {
        var path = Path.GetFullPath(folder);
        StorageService.EnsureWritableDirectory(path, "radio cache folder");
        RootPath = path;
        EnsureFolders();
    }

    public async Task<Track> KeepAsync(Track temporary, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(temporary.FilePath)) throw new FileNotFoundException("The temporary radio track is no longer available.", temporary.FilePath);
        var existing = await _library.FindExistingAsync(
            temporary.SourceVideoId, temporary.SourceUrl, temporary.Title, temporary.Artist, cancellationToken);
        if (existing is not null && File.Exists(existing.FilePath))
        {
            RemoveMetadata(temporary.FilePath);
            return existing;
        }
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
        var kept = await _library.AddOrUpdateTrackAsync(new Track
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
        RemoveMetadata(temporary.FilePath);
        return kept;
    }

    public async Task RegisterAsync(Track track, CancellationToken cancellationToken = default)
    {
        EnsureFolders();
        var metadata = new RadioCacheMetadata(
            track.Title, track.Artist, track.Album, track.ArtworkPath, track.DurationSeconds,
            track.SourceUrl, track.SourceVideoId, track.DateAdded);
        await File.WriteAllTextAsync(MetadataPath(track.FilePath), JsonSerializer.Serialize(metadata), cancellationToken);
    }

    public async Task<IReadOnlyList<Track>> GetCachedTracksAsync(CancellationToken cancellationToken = default)
    {
        EnsureFolders();
        var tracks = new List<Track>();
        var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var metadataPath in Directory.EnumerateFiles(TracksPath, "*.radio.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var audioPath = metadataPath[..^".radio.json".Length];
                if (!File.Exists(audioPath))
                {
                    File.Delete(metadataPath);
                    continue;
                }
                var metadata = JsonSerializer.Deserialize<RadioCacheMetadata>(await File.ReadAllTextAsync(metadataPath, cancellationToken));
                if (metadata is null) continue;
                tracks.Add(new Track
                {
                    Title = metadata.Title,
                    Artist = metadata.Artist,
                    Album = metadata.Album,
                    FilePath = audioPath,
                    ArtworkPath = metadata.ArtworkPath,
                    DurationSeconds = metadata.DurationSeconds,
                    SourceUrl = metadata.SourceUrl,
                    SourceVideoId = metadata.SourceVideoId,
                    DateAdded = metadata.DateAdded
                });
                indexedPaths.Add(audioPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        foreach (var audioPath in Directory.EnumerateFiles(TracksPath, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => AudioExtensions.Contains(Path.GetExtension(path)) && !indexedPaths.Contains(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracks.Add(new Track
            {
                Title = Path.GetFileNameWithoutExtension(audioPath),
                Artist = "Unknown Artist",
                FilePath = audioPath,
                ArtworkPath = FindLegacyArtwork(audioPath),
                DurationSeconds = ReadDuration(audioPath),
                DateAdded = File.GetLastWriteTimeUtc(audioPath)
            });
        }
        return tracks.OrderByDescending(track => track.DateAdded).ToArray();
    }

    public void Cleanup(IEnumerable<string> protectedPaths)
    {
        try
        {
            EnsureFolders();
            var protectedSet = protectedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".radio.json", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path)).Where(file => !protectedSet.Contains(file.FullName)).OrderBy(file => file.LastWriteTimeUtc).ToList();
            var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(_settings.GetRadioCacheMaxAgeDays(), 1, 90));
            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff).ToArray()) TryDelete(file, files);
            var maximumBytes = Math.Clamp(_settings.GetRadioCacheMaxSizeGb(), 0.25, 50) * 1024 * 1024 * 1024;
            var total = Directory.EnumerateFiles(RootPath, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".radio.json", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path).Length).Sum();
            foreach (var file in files.ToArray())
            {
                if (total <= maximumBytes) break;
                if (!file.Exists) continue;
                var length = file.Length;
                TryDelete(file, files);
                if (!file.Exists) total -= length;
            }
            foreach (var metadataPath in Directory.EnumerateFiles(TracksPath, "*.radio.json", SearchOption.TopDirectoryOnly))
                if (!File.Exists(metadataPath[..^".radio.json".Length])) TryDeletePath(metadataPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(FileInfo file, ICollection<FileInfo> files)
    {
        try
        {
            var path = file.FullName;
            file.Delete();
            files.Remove(file);
            RemoveMetadata(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string MetadataPath(string audioPath) => audioPath + ".radio.json";
    private static void RemoveMetadata(string audioPath) => TryDeletePath(MetadataPath(audioPath));
    private static void TryDeletePath(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string? FindLegacyArtwork(string audioPath)
    {
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        return new[] { ".jpg", ".jpeg", ".png", ".webp" }
            .Select(extension => Path.Combine(ArtworkPath, stem + extension))
            .FirstOrDefault(File.Exists);
    }

    private static double ReadDuration(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            return reader.TotalTime.TotalSeconds;
        }
        catch { return 0; }
    }

    private static string UniquePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;
        return Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
    }

    private sealed record RadioCacheMetadata(
        string Title,
        string Artist,
        string? Album,
        string? ArtworkPath,
        double DurationSeconds,
        string? SourceUrl,
        string? SourceVideoId,
        DateTime DateAdded);
}
