using System.Collections.ObjectModel;
using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class PlaylistViewModel : ObservableObject
{
    private readonly PlaylistService _playlists;
    private readonly PlayerViewModel _player;
    private Playlist? _selectedPlaylist;
    private Track? _selectedTrack;
    private string _playlistName = string.Empty;
    private string _statusText = string.Empty;

    public PlaylistViewModel(PlaylistService playlists, PlayerViewModel player)
    {
        _playlists = playlists;
        _player = player;
        CreateCommand = new AsyncCommand(CreateAsync, () => !string.IsNullOrWhiteSpace(PlaylistName));
        RenameCommand = new AsyncCommand(RenameAsync, () => SelectedPlaylist is not null && !string.IsNullOrWhiteSpace(PlaylistName));
        DeleteCommand = new AsyncCommand(DeleteAsync, () => SelectedPlaylist is not null);
        RemoveTrackCommand = new AsyncCommand(RemoveTrackAsync, () => SelectedPlaylist is not null && SelectedTrack is not null);
        MoveUpCommand = new AsyncCommand(() => MoveAsync(-1), () => CanMove(-1));
        MoveDownCommand = new AsyncCommand(() => MoveAsync(1), () => CanMove(1));
        PlayCommand = new RelayCommand(Play, () => SelectedTrack is not null);
        PlayAllCommand = new RelayCommand(PlayAll, () => Tracks.Count > 0);
        ShuffleCommand = new RelayCommand(PlayShuffled, () => Tracks.Count > 0);
    }

    public ObservableCollection<Playlist> Playlists { get; } = [];
    public ObservableCollection<Track> Tracks { get; } = [];
    public AsyncCommand CreateCommand { get; }
    public AsyncCommand RenameCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand RemoveTrackCommand { get; }
    public AsyncCommand MoveUpCommand { get; }
    public AsyncCommand MoveDownCommand { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand PlayAllCommand { get; }
    public RelayCommand ShuffleCommand { get; }

    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (!SetProperty(ref _selectedPlaylist, value)) return;
            PlaylistName = value?.Name ?? string.Empty;
            _ = LoadTracksAsync();
            RefreshCommands();
        }
    }

    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set { if (SetProperty(ref _selectedTrack, value)) RefreshCommands(); }
    }

    public string PlaylistName
    {
        get => _playlistName;
        set { if (SetProperty(ref _playlistName, value)) RefreshCommands(); }
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public async Task InitializeAsync()
    {
        Playlists.Clear();
        foreach (var playlist in await _playlists.GetPlaylistsAsync()) Playlists.Add(playlist);
        SelectedPlaylist ??= Playlists.FirstOrDefault();
    }

    public async Task AddTrackAsync(Track track)
    {
        if (SelectedPlaylist is null)
        {
            StatusText = "Choose or create a playlist first.";
            return;
        }
        await _playlists.AddTrackAsync(SelectedPlaylist.Id, track.Id);
        await LoadTracksAsync();
        StatusText = $"Added {track.Title} to {SelectedPlaylist.Name}.";
    }

    private async Task CreateAsync()
    {
        try
        {
            var playlist = await _playlists.CreateAsync(PlaylistName);
            Playlists.Add(playlist);
            SelectedPlaylist = playlist;
            StatusText = "Playlist created.";
        }
        catch (Exception ex) { StatusText = $"Could not create playlist: {ex.Message}"; }
    }

    private async Task RenameAsync()
    {
        if (SelectedPlaylist is null) return;
        try
        {
            var index = Playlists.IndexOf(SelectedPlaylist);
            var renamed = await _playlists.RenameAsync(SelectedPlaylist, PlaylistName);
            Playlists[index] = renamed;
            SelectedPlaylist = renamed;
            StatusText = "Playlist renamed.";
        }
        catch (Exception ex) { StatusText = $"Could not rename playlist: {ex.Message}"; }
    }

    private async Task DeleteAsync()
    {
        if (SelectedPlaylist is null) return;
        try
        {
            var deleting = SelectedPlaylist;
            await _playlists.DeleteAsync(deleting);
            Playlists.Remove(deleting);
            SelectedPlaylist = Playlists.FirstOrDefault();
            StatusText = "Playlist deleted. Audio files were not removed.";
        }
        catch (Exception ex) { StatusText = $"Could not delete playlist: {ex.Message}"; }
    }

    private async Task LoadTracksAsync()
    {
        try
        {
            Tracks.Clear();
            if (SelectedPlaylist is not null)
                foreach (var track in await _playlists.GetTracksAsync(SelectedPlaylist.Id)) Tracks.Add(track);
            RefreshCommands();
        }
        catch (Exception ex) { StatusText = $"Could not load playlist: {ex.Message}"; }
    }

    private async Task RemoveTrackAsync()
    {
        if (SelectedPlaylist is null || SelectedTrack is null) return;
        var track = SelectedTrack;
        await _playlists.RemoveTrackAsync(SelectedPlaylist.Id, track.Id);
        Tracks.Remove(track);
        SelectedTrack = null;
        StatusText = "Track removed from playlist.";
    }

    private bool CanMove(int direction)
    {
        if (SelectedTrack is null) return false;
        var index = Tracks.IndexOf(SelectedTrack);
        return index >= 0 && index + direction >= 0 && index + direction < Tracks.Count;
    }

    private async Task MoveAsync(int direction)
    {
        if (SelectedPlaylist is null || SelectedTrack is null) return;
        var index = Tracks.IndexOf(SelectedTrack);
        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= Tracks.Count) return;
        Tracks.Move(index, newIndex);
        await _playlists.SaveOrderAsync(SelectedPlaylist.Id, Tracks);
        RefreshCommands();
    }

    private void Play()
    {
        if (SelectedTrack is not null) _player.PlayTrack(SelectedTrack, Tracks.ToList());
    }

    private void PlayAll()
    {
        if (Tracks.Count > 0) _player.PlayTrack(Tracks[0], Tracks.ToList());
    }

    private void PlayShuffled()
    {
        if (Tracks.Count == 0) return;
        _player.Shuffle = true;
        _player.PlayTrack(Tracks[Random.Shared.Next(Tracks.Count)], Tracks.ToList());
    }

    private void RefreshCommands()
    {
        CreateCommand.RaiseCanExecuteChanged();
        RenameCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        RemoveTrackCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        PlayCommand.RaiseCanExecuteChanged();
        PlayAllCommand.RaiseCanExecuteChanged();
        ShuffleCommand.RaiseCanExecuteChanged();
    }
}
