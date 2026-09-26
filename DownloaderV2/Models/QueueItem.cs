using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DownloaderV2.Models;

public enum QueueItemStatus { PendingDiscovery, Resolving, Downloading, Ready, Playing, Failed, Skipped }

public sealed class QueueItem : INotifyPropertyChanged
{
    private Track? _localTrack;
    private QueueItemStatus _status;
    private double _progress;

    public Guid Id { get; } = Guid.NewGuid();
    public DiscoveryTrack? PendingDiscovery { get; init; }
    public bool IsRadioGenerated { get; init; }
    public bool IsTemporary { get; set; }
    public Track? LocalTrack { get => _localTrack; set { if (_localTrack != value) { _localTrack = value; Changed(); Changed(nameof(Title)); Changed(nameof(Artist)); Changed(nameof(ArtworkPath)); } } }
    public QueueItemStatus Status { get => _status; set { if (_status != value) { _status = value; Changed(); Changed(nameof(StatusText)); } } }
    public double Progress { get => _progress; set { if (Math.Abs(_progress - value) > 0.01) { _progress = value; Changed(); Changed(nameof(StatusText)); } } }
    public string Title => LocalTrack?.Title ?? PendingDiscovery?.Title ?? "Finding recommendation…";
    public string Artist => LocalTrack?.Artist ?? PendingDiscovery?.Artist ?? string.Empty;
    public string? ArtworkPath => LocalTrack?.ArtworkPath ?? PendingDiscovery?.ThumbnailUrl;
    public string StatusText => Status == QueueItemStatus.Downloading && Progress > 0 ? $"Downloading {Progress:0}%" : Status.ToString();
    public string RadioText => IsRadioGenerated ? "Radio" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
