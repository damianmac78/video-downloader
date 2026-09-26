using System.Collections.ObjectModel;
using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class DiscoveryViewModel : ObservableObject
{
    private readonly IMusicDiscoveryService _discovery;
    private readonly AudioLibraryDownloadService _downloader;
    private readonly PlaylistViewModel _playlists;
    private readonly PlayerViewModel _player;
    private CancellationTokenSource? _cancellation;
    private Track? _sourceTrack;
    private DiscoveryTrack? _selectedResult;
    private string _statusText = string.Empty;
    private string _technicalDetails = string.Empty;
    private bool _isBusy;
    private bool _isDiscovering;

    public DiscoveryViewModel(IMusicDiscoveryService discovery, AudioLibraryDownloadService downloader, PlaylistViewModel playlists, PlayerViewModel player)
    {
        _discovery = discovery;
        _downloader = downloader;
        _playlists = playlists;
        _player = player;
        DownloadCommand = new AsyncCommand(DownloadAsync, () => SelectedResult is { IsAlreadyInLibrary: false } && !IsBusy);
        AddToPlaylistCommand = new AsyncCommand(AddToPlaylistAsync, () => SelectedResult?.ExistingTrack is not null && !IsBusy);
        PlayCommand = new RelayCommand(Play, () => SelectedResult?.ExistingTrack is not null && !IsBusy);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);
    }

    public event EventHandler<Track>? TrackAdded;
    public ObservableCollection<DiscoveryTrack> Results { get; } = [];
    public AsyncCommand DownloadCommand { get; }
    public AsyncCommand AddToPlaylistCommand { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand CancelCommand { get; }
    public string SourceDescription
    {
        get
        {
            if (SourceTrack is null) return string.Empty;
            var (artist, title) = MusicMetadata.Clean(SourceTrack);
            return $"Similar to {artist} — {title}";
        }
    }
    public Track? SourceTrack { get => _sourceTrack; private set { if (SetProperty(ref _sourceTrack, value)) OnPropertyChanged(nameof(SourceDescription)); } }
    public DiscoveryTrack? SelectedResult { get => _selectedResult; set { if (SetProperty(ref _selectedResult, value)) RefreshCommands(); } }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string TechnicalDetails { get => _technicalDetails; private set { if (SetProperty(ref _technicalDetails, value)) OnPropertyChanged(nameof(HasTechnicalDetails)); } }
    public bool HasTechnicalDetails => !string.IsNullOrWhiteSpace(TechnicalDetails);
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RefreshCommands(); } }
    public bool IsDiscovering { get => _isDiscovering; private set => SetProperty(ref _isDiscovering, value); }

    public async Task LoadAsync(Track track)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        SourceTrack = track;
        Results.Clear();
        SelectedResult = null;
        TechnicalDetails = string.Empty;
        IsBusy = true;
        IsDiscovering = true;
        StatusText = "Finding similar music…";
        try
        {
            var results = await _discovery.FindSimilarAsync(track, _cancellation.Token);
            foreach (var result in results) Results.Add(result);
            StatusText = results.Count == 0 ? "No discovery results were found." : $"{results.Count} recommendations found. Nothing is downloaded automatically.";
        }
        catch (OperationCanceledException) { StatusText = "Discovery cancelled."; }
        catch (LastFmException ex) { StatusText = ex.Message; }
        catch (YtDlpException ex) { StatusText = ex.Message; TechnicalDetails = ex.Details; }
        catch (Exception ex) { StatusText = "Music discovery failed."; TechnicalDetails = ex.Message; }
        finally { IsDiscovering = false; IsBusy = false; }
    }

    private async Task DownloadAsync()
    {
        if (SelectedResult is null) return;
        IsBusy = true;
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        SelectedResult.Status = "Analysing";
        try
        {
            var info = new VideoInfo(SelectedResult.Title, SelectedResult.Artist, SelectedResult.Duration,
                SelectedResult.ThumbnailUrl, SelectedResult.VideoId, SelectedResult.SourceUrl);
            var progress = new Progress<DownloadProgress>(value => SelectedResult.Status = value.Percentage > 0 ? $"Downloading {value.Percentage:0}%" : "Downloading");
            var result = await _downloader.DownloadAsync(SelectedResult.SourceUrl, info, progress: progress, cancellationToken: _cancellation.Token);
            SelectedResult.ExistingTrack = result.Track;
            SelectedResult.Status = result.WasAlreadyInLibrary ? "Already in Music" : "Added to Music";
            if (!result.WasAlreadyInLibrary) TrackAdded?.Invoke(this, result.Track);
            StatusText = SelectedResult.Status;
        }
        catch (OperationCanceledException) { SelectedResult.Status = "Cancelled"; StatusText = "Download cancelled."; }
        catch (YtDlpException ex) { SelectedResult.Status = "Failed"; StatusText = ex.Message; TechnicalDetails = ex.Details; }
        catch (Exception ex) { SelectedResult.Status = "Failed"; StatusText = "The track could not be added to Music."; TechnicalDetails = ex.Message; }
        finally { IsBusy = false; RefreshCommands(); }
    }

    private async Task AddToPlaylistAsync()
    {
        if (SelectedResult?.ExistingTrack is null) return;
        await _playlists.AddTrackAsync(SelectedResult.ExistingTrack);
        StatusText = "Added to the selected local playlist.";
    }

    private void Play()
    {
        if (SelectedResult?.ExistingTrack is not null) _player.PlayTrack(SelectedResult.ExistingTrack, [SelectedResult.ExistingTrack]);
    }

    private void RefreshCommands()
    {
        DownloadCommand.RaiseCanExecuteChanged();
        AddToPlaylistCommand.RaiseCanExecuteChanged();
        PlayCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }
}
