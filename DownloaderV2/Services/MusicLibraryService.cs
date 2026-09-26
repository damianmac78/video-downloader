using DownloaderV2.Data;
using DownloaderV2.Models;
using Microsoft.Data.Sqlite;
using NAudio.Wave;

namespace DownloaderV2.Services;

public sealed class MusicLibraryService(AppDbContext database, string libraryRoot, string? tracksFolder = null)
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m4a", ".mp3", ".aac", ".opus", ".ogg", ".webm", ".wav", ".flac", ".aiff", ".wma"
    };

    public string LibraryRoot { get; } = libraryRoot;
    public string TracksFolder { get; private set; } = Path.GetFullPath(tracksFolder ?? Path.Combine(libraryRoot, "Tracks"));
    public string ArtworkFolder { get; } = Path.Combine(libraryRoot, "Artwork");

    public void EnsureFolders()
    {
        Directory.CreateDirectory(LibraryRoot);
        Directory.CreateDirectory(TracksFolder);
        Directory.CreateDirectory(ArtworkFolder);
    }

    public void SetTracksFolder(string folder)
    {
        var path = Path.GetFullPath(folder);
        StorageService.EnsureWritableDirectory(path, "music download folder");
        TracksFolder = path;
    }

    public async Task<IReadOnlyList<Track>> GetTracksAsync(CancellationToken cancellationToken = default)
    {
        var tracks = new List<Track>();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Artist, Album, FilePath, ArtworkPath, DurationSeconds,
                   SourceUrl, SourceVideoId, DateAdded
            FROM Tracks
            ORDER BY DateAdded DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) tracks.Add(ReadTrack(reader));
        return tracks;
    }

    public async Task<Track?> FindExistingAsync(
        string? sourceVideoId,
        string? sourceUrl,
        string? title = null,
        string? artist = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Artist, Album, FilePath, ArtworkPath, DurationSeconds,
                   SourceUrl, SourceVideoId, DateAdded
            FROM Tracks
            WHERE ($videoId IS NOT NULL AND SourceVideoId = $videoId)
               OR ($sourceUrl IS NOT NULL AND SourceUrl = $sourceUrl)
               OR ($title IS NOT NULL AND $artist IS NOT NULL
                   AND Title = $title COLLATE NOCASE AND Artist = $artist COLLATE NOCASE)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$videoId", (object?)NullIfWhiteSpace(sourceVideoId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceUrl", (object?)NullIfWhiteSpace(sourceUrl) ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", (object?)NullIfWhiteSpace(title) ?? DBNull.Value);
        command.Parameters.AddWithValue("$artist", (object?)NullIfWhiteSpace(artist) ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken)) return ReadTrack(reader);
        await reader.DisposeAsync();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return null;
        var identity = MusicMetadata.Identity(artist, title);
        return (await GetTracksAsync(cancellationToken))
            .FirstOrDefault(track => MusicMetadata.Identity(track.Artist, track.Title) == identity);
    }

    public async Task<Track> AddOrUpdateTrackAsync(Track track, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Tracks
                (Title, Artist, Album, FilePath, ArtworkPath, DurationSeconds, SourceUrl, SourceVideoId, DateAdded)
            VALUES
                ($title, $artist, $album, $filePath, $artworkPath, $duration, $sourceUrl, $sourceVideoId, $dateAdded)
            ON CONFLICT(FilePath) DO UPDATE SET
                Title = excluded.Title,
                Artist = excluded.Artist,
                Album = excluded.Album,
                ArtworkPath = excluded.ArtworkPath,
                DurationSeconds = excluded.DurationSeconds,
                SourceUrl = excluded.SourceUrl,
                SourceVideoId = excluded.SourceVideoId
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$title", track.Title);
        command.Parameters.AddWithValue("$artist", track.Artist);
        command.Parameters.AddWithValue("$album", (object?)track.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$filePath", track.FilePath);
        command.Parameters.AddWithValue("$artworkPath", (object?)track.ArtworkPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration", track.DurationSeconds);
        command.Parameters.AddWithValue("$sourceUrl", (object?)track.SourceUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceVideoId", (object?)track.SourceVideoId ?? DBNull.Value);
        command.Parameters.AddWithValue("$dateAdded", track.DateAdded.ToUniversalTime().ToString("O"));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        return new Track
        {
            Id = id,
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            FilePath = track.FilePath,
            ArtworkPath = track.ArtworkPath,
            DurationSeconds = track.DurationSeconds,
            SourceUrl = track.SourceUrl,
            SourceVideoId = track.SourceVideoId,
            DateAdded = track.DateAdded
        };
    }

    public async Task RemoveTrackAsync(Track track, bool deleteFile, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Tracks WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", track.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (deleteFile && File.Exists(track.FilePath)) File.Delete(track.FilePath);
    }

    public async Task<MusicFolderScanResult> ScanFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"The folder does not exist: {root}");

        var files = await Task.Run(() => Directory
            .EnumerateFiles(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            })
            .Where(path => AudioExtensions.Contains(Path.GetExtension(path)))
            .Select(Path.GetFullPath)
            .ToArray(), cancellationToken);

        var existing = await GetTracksAsync(cancellationToken);
        var knownPaths = existing.Select(track => Path.GetFullPath(track.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var movedByFileName = existing
            .Where(track => !File.Exists(track.FilePath))
            .GroupBy(track => Path.GetFileName(track.FilePath), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var relinkedIds = new HashSet<long>();
        var added = 0;
        var relinked = 0;
        var unchanged = 0;
        var failed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (knownPaths.Contains(file))
            {
                unchanged++;
                continue;
            }

            try
            {
                var artworkPath = FindArtwork(file);
                var fileName = Path.GetFileName(file);
                if (movedByFileName.TryGetValue(fileName, out var moved) && relinkedIds.Add(moved.Id))
                {
                    await UpdateTrackLocationAsync(moved.Id, file, artworkPath, cancellationToken);
                    relinked++;
                    knownPaths.Add(file);
                    continue;
                }

                var (artist, title) = ParseFileName(file);
                var created = File.GetCreationTimeUtc(file);
                if (created.Year < 1971) created = DateTime.UtcNow;
                await AddOrUpdateTrackAsync(new Track
                {
                    Title = title,
                    Artist = artist,
                    FilePath = file,
                    ArtworkPath = artworkPath,
                    DurationSeconds = GetDurationSeconds(file),
                    DateAdded = created
                }, cancellationToken);
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
            {
                failed++;
            }
        }

        return new MusicFolderScanResult(files.Length, added, relinked, unchanged, failed);
    }

    private async Task UpdateTrackLocationAsync(long id, string filePath, string? artworkPath, CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Tracks
            SET FilePath = $filePath,
                ArtworkPath = CASE WHEN $artworkPath IS NULL THEN ArtworkPath ELSE $artworkPath END
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$filePath", filePath);
        command.Parameters.AddWithValue("$artworkPath", (object?)artworkPath ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (string Artist, string Title) ParseFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        var separator = name.IndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0 || separator >= name.Length - 3) return ("Unknown Artist", name);
        return (name[..separator].Trim(), name[(separator + 3)..].Trim());
    }

    private static string? FindArtwork(string audioPath)
    {
        var directory = Path.GetDirectoryName(audioPath)!;
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var besideTrack = Path.Combine(directory, stem + extension);
            if (File.Exists(besideTrack)) return besideTrack;
        }
        foreach (var name in new[] { "folder.jpg", "cover.jpg", "folder.png", "cover.png" })
        {
            var shared = Path.Combine(directory, name);
            if (File.Exists(shared)) return shared;
        }
        return null;
    }

    private static double GetDurationSeconds(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            return reader.TotalTime.TotalSeconds;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or NotSupportedException)
        {
            return 0;
        }
    }

    internal static Track ReadTrack(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Title = reader.GetString(1),
        Artist = reader.GetString(2),
        Album = reader.IsDBNull(3) ? null : reader.GetString(3),
        FilePath = reader.GetString(4),
        ArtworkPath = reader.IsDBNull(5) ? null : reader.GetString(5),
        DurationSeconds = reader.GetDouble(6),
        SourceUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
        SourceVideoId = reader.IsDBNull(8) ? null : reader.GetString(8),
        DateAdded = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed record MusicFolderScanResult(int FilesFound, int Added, int Relinked, int Unchanged, int Failed);
