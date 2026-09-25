using Microsoft.Data.Sqlite;

namespace DownloaderV2.Data;

public sealed class AppDbContext
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public AppDbContext(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
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
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var existingVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (existingVersion > CurrentSchemaVersion)
            throw new InvalidOperationException($"The music library database uses schema version {existingVersion}, but this application supports up to version {CurrentSchemaVersion}.");

        if (existingVersion == CurrentSchemaVersion) return;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (existingVersion < 1) await ApplyVersionOneAsync(connection, transaction, cancellationToken);
            await using var updateVersion = connection.CreateCommand();
            updateVersion.Transaction = transaction;
            updateVersion.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            await updateVersion.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ApplyVersionOneAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
