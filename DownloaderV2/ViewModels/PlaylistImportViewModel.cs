using System.Collections.ObjectModel;
using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class PlaylistImportViewModel : ObservableObject
{
    private readonly PlaylistImportService _importService;
    private readonly AudioLibraryDownloadService _downloader;
    private readonly PlaylistService _playlists;
    private CancellationTokenSource? _cancellation;
    private string _title = string.Empty;
    private string _statusText = string.Empty;
    private string _technicalDetails = string.Empty;
    private bool _hasPlaylist;
    private bool _isBusy;

    public PlaylistImportViewModel(PlaylistImportService importService, AudioLibraryDownloadService downloader, PlaylistService playlists)
    {
        _importService = importService;
        _downloader = downloader;
        _playlists = playlists;
        SelectAllCommand = new RelayCommand(() => SetSelection(true), () => HasPlaylist && !IsBusy);
        SelectNoneCommand = new RelayCommand(() => SetSelection(false), () => HasPlaylist && !IsBusy);
        DownloadSelectedCommand = new AsyncCommand(DownloadSelectedAsync, () => HasPlaylist && !IsBusy);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);
    }

    public event EventHandler<Track>? TrackAdded;
    public event EventHandler? Completed;
    public ObservableCollection<PlaylistImportTrack> Entries { get; } = [];
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public AsyncCommand DownloadSelectedCommand { get; }
    public RelayCommand CancelCommand { get; }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string TechnicalDetails { get => _technicalDetails; private set => SetProperty(ref _technicalDetails, value); }
    public bool HasTechnicalDetails => !string.IsNullOrWhiteSpace(TechnicalDetails);
    public bool HasPlaylist { get => _hasPlaylist; private set { if (SetProperty(ref _hasPlaylist, value)) RefreshCommands(); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RefreshCommands(); } }

    public bool IsPlaylistUrl(string url) => _importService.IsPlaylistUrl(url);

    public async Task LoadAsync(string originalUrl, CancellationToken cancellationToken = default)
    {
        Clear();
        IsBusy = true;
        StatusText = "Inspecting playlist…";
        try
        {
            var import = await _importService.AnalyzeAsync(originalUrl, cancellationToken);
            Title = import.Title;
            foreach (var entry in import.Entries) Entries.Add(entry);
            HasPlaylist = true;
            var available = Entries.Count(entry => entry.IsAvailable);
            StatusText = $"{Entries.Count} entries found; {available} available. Choose tracks, then download selected.";
        }
        catch (YtDlpException ex)
        {
            StatusText = ex.Message;
            TechnicalDetails = ex.Details;
            OnPropertyChanged(nameof(HasTechnicalDetails));
        }
        finally { IsBusy = false; }
    }

    public void Clear()
    {
        _cancellation?.Cancel();
        Entries.Clear();
        Title = string.Empty;
        StatusText = string.Empty;
        TechnicalDetails = string.Empty;
        OnPropertyChanged(nameof(HasTechnicalDetails));
        HasPlaylist = false;
    }

    private async Task DownloadSelectedAsync()
    {
        var selected = Entries.Where(entry => entry.IsSelected && entry.IsAvailable).OrderBy(entry => entry.Index).ToArray();
        if (selected.Length == 0) { StatusText = "Select at least one available track."; return; }

        IsBusy = true;
        TechnicalDetails = string.Empty;
        OnPropertyChanged(nameof(HasTechnicalDetails));
        _cancellation = new CancellationTokenSource();
        var complete = 0;
        var failed = 0;
        try
        {
            var localPlaylist = await _playlists.GetOrCreateAsync(Title, _cancellation.Token);
            for (var position = 0; position < selected.Length; position++)
            {
                var entry = selected[position];
                _cancellation.Token.ThrowIfCancellationRequested();
                entry.Status = "Analysing";
                StatusText = $"Track {position + 1} of {selected.Length}: {entry.Title}";
                try
                {
                    var info = new VideoInfo(entry.Title, entry.Uploader, entry.Duration, entry.ThumbnailUrl, entry.VideoId, entry.SourceUrl);
                    var progress = new Progress<DownloadProgress>(value => entry.Status = value.Percentage > 0 ? $"Downloading {value.Percentage:0}%" : "Downloading");
                    var result = await _downloader.DownloadAsync(entry.SourceUrl!, info, progress: progress, cancellationToken: _cancellation.Token);
                    await _playlists.AddTrackAsync(localPlaylist.Id, result.Track.Id, _cancellation.Token);
                    entry.Status = result.WasAlreadyInLibrary ? "Already in Music" : "Complete";
                    if (!result.WasAlreadyInLibrary) TrackAdded?.Invoke(this, result.Track);
                    complete++;
                }
                catch (OperationCanceledException) { throw; }
                catch (YtDlpException ex)
                {
                    entry.Status = "Failed";
                    failed++;
                    TechnicalDetails += $"[{entry.Title}]{Environment.NewLine}{ex.Details}{Environment.NewLine}{Environment.NewLine}";
                    OnPropertyChanged(nameof(HasTechnicalDetails));
                }
                catch (Exception ex)
                {
                    entry.Status = "Failed";
                    failed++;
                    TechnicalDetails += $"[{entry.Title}]{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}";
                    OnPropertyChanged(nameof(HasTechnicalDetails));
                }
            }
            StatusText = $"Playlist import finished: {complete} complete or already present, {failed} failed.";
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            foreach (var entry in selected.Where(entry => entry.Status is "Queued" or "Analysing" || entry.Status.StartsWith("Downloading", StringComparison.Ordinal))) entry.Status = "Skipped";
            StatusText = "Playlist download cancelled.";
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsBusy = false;
        }
    }

    private void SetSelection(bool selected)
    {
        foreach (var entry in Entries.Where(entry => entry.IsAvailable)) entry.IsSelected = selected;
    }

    private void RefreshCommands()
    {
        SelectAllCommand.RaiseCanExecuteChanged();
        SelectNoneCommand.RaiseCanExecuteChanged();
        DownloadSelectedCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }
}
