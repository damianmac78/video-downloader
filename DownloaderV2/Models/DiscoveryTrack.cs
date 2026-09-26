using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DownloaderV2.Models;

public sealed class DiscoveryTrack : INotifyPropertyChanged
{
    private Track? _existingTrack;
    private string _status = string.Empty;

    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string SourceUrl { get; init; } = string.Empty;
    public string? VideoId { get; init; }
    public double? LastFmSimilarity { get; init; }
    public string DurationText => Duration?.ToString(Duration.Value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") ?? "—";
    public string SimilarityText => LastFmSimilarity is { } similarity ? $"{similarity:P0}" : "—";
    public bool IsAlreadyInLibrary => ExistingTrack is not null;
    public Track? ExistingTrack { get => _existingTrack; set { if (_existingTrack != value) { _existingTrack = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAlreadyInLibrary)); } } }
    public string Status { get => _status; set { if (_status != value) { _status = value; OnPropertyChanged(); } } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record AudioLibraryDownloadResult(Track Track, bool WasAlreadyInLibrary);
