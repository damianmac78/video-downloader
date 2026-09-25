using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DownloaderV2.Models;

public sealed record PlaylistImport(string Title, string SourceUrl, IReadOnlyList<PlaylistImportTrack> Entries);

public sealed class PlaylistImportTrack : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _status = "Queued";

    public int Index { get; init; }
    public string? SourceUrl { get; init; }
    public string? VideoId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Uploader { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
    public string? ThumbnailUrl { get; init; }
    public bool IsAvailable => !string.IsNullOrWhiteSpace(SourceUrl);
    public string DurationText => Duration?.ToString(Duration.Value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") ?? "—";
    public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } } }
    public string Status { get => _status; set { if (_status != value) { _status = value; OnPropertyChanged(); } } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
