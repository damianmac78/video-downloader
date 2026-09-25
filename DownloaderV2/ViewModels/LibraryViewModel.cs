using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class LibraryViewModel : ObservableObject
{
    private readonly MusicLibraryService _library;
    private readonly PlayerViewModel _player;
    private Track? _selectedTrack;
    private string _searchText = string.Empty;
    private string _selectedSort = "Newest";
    private string _statusText = string.Empty;

    public LibraryViewModel(MusicLibraryService library, PlayerViewModel player, PlaylistViewModel playlists)
    {
        _library = library;
        _player = player;
        Playlists = playlists;
        TracksView = CollectionViewSource.GetDefaultView(Tracks);
        TracksView.Filter = FilterTrack;
        ApplySort();

        RefreshCommand = new AsyncCommand(RefreshAsync);
        PlayCommand = new RelayCommand(PlaySelected, () => SelectedTrack is not null);
        AddToPlaylistCommand = new AsyncCommand(AddToPlaylistAsync, () => SelectedTrack is not null);
        DiscoverCommand = new RelayCommand(() => DiscoverRequested?.Invoke(this, SelectedTrack!), () => SelectedTrack is not null);
    }

    public ObservableCollection<Track> Tracks { get; } = [];
    public ICollectionView TracksView { get; }
    public PlaylistViewModel Playlists { get; }
    public IReadOnlyList<string> SortOptions { get; } = ["Newest", "Title", "Artist", "Duration"];
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand PlayCommand { get; }
    public AsyncCommand AddToPlaylistCommand { get; }
    public RelayCommand DiscoverCommand { get; }
    public event EventHandler<Track>? DiscoverRequested;

    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (SetProperty(ref _selectedTrack, value))
            {
                PlayCommand.RaiseCanExecuteChanged();
                AddToPlaylistCommand.RaiseCanExecuteChanged();
                DiscoverCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) TracksView.Refresh(); }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set { if (SetProperty(ref _selectedSort, value)) ApplySort(); }
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public async Task InitializeAsync()
    {
        await Playlists.InitializeAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var selectedId = SelectedTrack?.Id;
            Tracks.Clear();
            foreach (var track in await _library.GetTracksAsync()) Tracks.Add(track);
            SelectedTrack = Tracks.FirstOrDefault(track => track.Id == selectedId);
            TracksView.Refresh();
            StatusText = $"{Tracks.Count} track{(Tracks.Count == 1 ? string.Empty : "s")} in library.";
        }
        catch (Exception ex) { StatusText = $"Could not load library: {ex.Message}"; }
    }

    public async Task DeleteSelectedAsync(bool deleteUnderlyingFile)
    {
        if (SelectedTrack is null) return;
        try
        {
            var deleting = SelectedTrack;
            await _library.RemoveTrackAsync(deleting, deleteUnderlyingFile);
            Tracks.Remove(deleting);
            SelectedTrack = null;
            StatusText = deleteUnderlyingFile ? "Track and audio file deleted." : "Track removed from library.";
        }
        catch (Exception ex) { StatusText = $"Could not delete track: {ex.Message}"; }
    }

    private bool FilterTrack(object item)
    {
        if (item is not Track track || string.IsNullOrWhiteSpace(SearchText)) return true;
        return track.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               track.Artist.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               (track.Album?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void ApplySort()
    {
        TracksView.SortDescriptions.Clear();
        TracksView.SortDescriptions.Add(SelectedSort switch
        {
            "Title" => new SortDescription(nameof(Track.Title), ListSortDirection.Ascending),
            "Artist" => new SortDescription(nameof(Track.Artist), ListSortDirection.Ascending),
            "Duration" => new SortDescription(nameof(Track.DurationSeconds), ListSortDirection.Descending),
            _ => new SortDescription(nameof(Track.DateAdded), ListSortDirection.Descending)
        });
    }

    private void PlaySelected()
    {
        if (SelectedTrack is null) return;
        var queue = TracksView.Cast<Track>().ToList();
        _player.PlayTrack(SelectedTrack, queue);
    }

    private async Task AddToPlaylistAsync()
    {
        if (SelectedTrack is not null) await Playlists.AddTrackAsync(SelectedTrack);
    }
}
