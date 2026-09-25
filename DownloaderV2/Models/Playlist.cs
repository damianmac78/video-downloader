namespace DownloaderV2.Models;

public sealed class Playlist
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTime DateCreated { get; init; }

    public override string ToString() => Name;
}
