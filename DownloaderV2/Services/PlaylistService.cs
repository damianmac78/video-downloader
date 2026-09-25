using DownloaderV2.Data;
using DownloaderV2.Models;

namespace DownloaderV2.Services;

public sealed class PlaylistService(AppDbContext database)
{
    public async Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        var playlists = new List<Playlist>();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, DateCreated FROM Playlists ORDER BY Name COLLATE NOCASE;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            playlists.Add(new Playlist
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                DateCreated = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }
        return playlists;
    }

    public async Task<Playlist> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var created = DateTime.UtcNow;
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Playlists (Name, DateCreated) VALUES ($name, $created) RETURNING Id;";
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$created", created.ToString("O"));
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return new Playlist { Id = id, Name = name.Trim(), DateCreated = created };
    }

    public async Task<Playlist> RenameAsync(Playlist playlist, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Playlists SET Name = $name WHERE Id = $id;";
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$id", playlist.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new Playlist { Id = playlist.Id, Name = name.Trim(), DateCreated = playlist.DateCreated };
    }

    public async Task DeleteAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; DELETE FROM Playlists WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", playlist.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Track>> GetTracksAsync(long playlistId, CancellationToken cancellationToken = default)
    {
        var tracks = new List<Track>();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.Id, t.Title, t.Artist, t.Album, t.FilePath, t.ArtworkPath,
                   t.DurationSeconds, t.SourceUrl, t.SourceVideoId, t.DateAdded
            FROM PlaylistTracks pt
            INNER JOIN Tracks t ON t.Id = pt.TrackId
            WHERE pt.PlaylistId = $playlistId
            ORDER BY pt.SortOrder;
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) tracks.Add(MusicLibraryService.ReadTrack(reader));
        return tracks;
    }

    public async Task AddTrackAsync(long playlistId, long trackId, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO PlaylistTracks (PlaylistId, TrackId, SortOrder)
            VALUES ($playlistId, $trackId,
                COALESCE((SELECT MAX(SortOrder) + 1 FROM PlaylistTracks WHERE PlaylistId = $playlistId), 0));
            """;
        command.Parameters.AddWithValue("$playlistId", playlistId);
        command.Parameters.AddWithValue("$trackId", trackId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveTrackAsync(long playlistId, long trackId, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PlaylistTracks WHERE PlaylistId = $playlistId AND TrackId = $trackId;";
        command.Parameters.AddWithValue("$playlistId", playlistId);
        command.Parameters.AddWithValue("$trackId", trackId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveOrderAsync(long playlistId, IReadOnlyList<Track> tracks, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        for (var index = 0; index < tracks.Count; index++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText = "UPDATE PlaylistTracks SET SortOrder = $order WHERE PlaylistId = $playlistId AND TrackId = $trackId;";
            command.Parameters.AddWithValue("$order", index);
            command.Parameters.AddWithValue("$playlistId", playlistId);
            command.Parameters.AddWithValue("$trackId", tracks[index].Id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
