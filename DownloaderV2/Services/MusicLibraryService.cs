using DownloaderV2.Data;
using DownloaderV2.Models;
using Microsoft.Data.Sqlite;

namespace DownloaderV2.Services;

public sealed class MusicLibraryService(AppDbContext database, string libraryRoot)
{
    public string LibraryRoot { get; } = libraryRoot;
    public string TracksFolder { get; } = Path.Combine(libraryRoot, "Tracks");
    public string ArtworkFolder { get; } = Path.Combine(libraryRoot, "Artwork");

    public void EnsureFolders()
    {
        Directory.CreateDirectory(LibraryRoot);
        Directory.CreateDirectory(TracksFolder);
        Directory.CreateDirectory(ArtworkFolder);
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
}
