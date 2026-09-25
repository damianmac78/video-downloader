using Microsoft.Data.Sqlite;

namespace DownloaderV2.Data;

public sealed class AppDbContext
{
    private readonly string _connectionString;

    public AppDbContext(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection CreateConnection() => new(_connectionString);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Tracks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Artist TEXT NOT NULL,
                Album TEXT NULL,
                FilePath TEXT NOT NULL UNIQUE,
                ArtworkPath TEXT NULL,
                DurationSeconds REAL NOT NULL,
                SourceUrl TEXT NULL,
                SourceVideoId TEXT NULL,
                DateAdded TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Tracks_SourceVideoId ON Tracks(SourceVideoId);

            CREATE TABLE IF NOT EXISTS Playlists (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                DateCreated TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PlaylistTracks (
                PlaylistId INTEGER NOT NULL,
                TrackId INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                PRIMARY KEY (PlaylistId, TrackId),
                FOREIGN KEY (PlaylistId) REFERENCES Playlists(Id) ON DELETE CASCADE,
                FOREIGN KEY (TrackId) REFERENCES Tracks(Id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
