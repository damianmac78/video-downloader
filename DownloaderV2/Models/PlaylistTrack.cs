namespace DownloaderV2.Models;

public sealed class PlaylistTrack
{
    public long PlaylistId { get; init; }
    public long TrackId { get; init; }
    public int SortOrder { get; init; }
}
