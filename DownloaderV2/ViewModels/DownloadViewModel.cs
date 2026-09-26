using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Win32;
using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class DownloadViewModel : ObservableObject
{
    private readonly YtDlpService _ytDlpService;
    private readonly SettingsService _settingsService;
    private readonly MusicLibraryService _musicLibrary;
    private readonly AudioLibraryDownloadService _audioDownloader;
    private CancellationTokenSource? _downloadCancellation;
    private AnalysisResult? _analysisResult;
    private string _url = string.Empty;
    private string _normalisedUrl = string.Empty;
    private VideoInfo? _video;
    private VideoFormatOption _selectedFormat;
    private string _downloadFolder;
    private double _progressPercentage;
    private string _speed = "—";
    private string _eta = "—";
    private string _statusText = "Paste a video URL to begin.";
    private string _errorDetails = string.Empty;
    private bool _isAnalysing;
    private bool _isDownloading;
    private bool _isComplete;

    public DownloadViewModel(
        YtDlpService ytDlpService,
        SettingsService settingsService,
        MusicLibraryService musicLibrary,
        AudioLibraryDownloadService audioDownloader,
        PlaylistImportViewModel playlistImport)
    {
        _ytDlpService = ytDlpService;
        _settingsService = settingsService;
        _musicLibrary = musicLibrary;
        _audioDownloader = audioDownloader;
        PlaylistImport = playlistImport;
        PlaylistImport.TrackAdded += (_, track) => AudioTrackAdded?.Invoke(this, track);
        PlaylistImport.Completed += (_, _) => DownloadCompleted?.Invoke(this, EventArgs.Empty);
        _musicLibrary.EnsureFolders();
        _downloadFolder = settingsService.GetDownloadFolder();
        _selectedFormat = VideoFormatOption.Defaults[0];
        Formats = new ObservableCollection<VideoFormatOption>(VideoFormatOption.Defaults);
        AnalyseCommand = new AsyncCommand(AnalyseAsync, CanAnalyse);
        DownloadCommand = new AsyncCommand(DownloadAsync, CanDownload);
        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy && !IsAudioOnly);
        CancelCommand = new RelayCommand(CancelDownload, () => IsDownloading);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Directory.Exists(OutputFolder));
    }

    public event EventHandler<Track>? AudioTrackAdded;
    public event EventHandler? DownloadCompleted;
    public ObservableCollection<VideoFormatOption> Formats { get; }
    public AsyncCommand AnalyseCommand { get; }
    public AsyncCommand DownloadCommand { get; }
    public AsyncCommand BrowseCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public PlaylistImportViewModel PlaylistImport { get; }

    public string Url
    {
        get => _url;
        set
        {
            if (!SetProperty(ref _url, value)) return;
            if (Video is not null && !string.Equals(value, _normalisedUrl, StringComparison.Ordinal))
            {
                Video = null;
                _analysisResult = null;
                StatusText = "URL changed. Analyse the video again.";
                IsComplete = false;
            }
            if (PlaylistImport.HasPlaylist && !string.Equals(value, _normalisedUrl, StringComparison.Ordinal))
            {
                PlaylistImport.Clear();
                StatusText = "URL changed. Analyse the playlist again.";
            }
            RefreshCommands();
        }
    }

    public VideoInfo? Video { get => _video; private set { if (SetProperty(ref _video, value)) { OnPropertyChanged(nameof(HasVideo)); RefreshCommands(); } } }

    public VideoFormatOption SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (!SetProperty(ref _selectedFormat, value)) return;
            OnPropertyChanged(nameof(IsAudioOnly));
            OnPropertyChanged(nameof(OutputFolder));
            BrowseCommand.RaiseCanExecuteChanged();
            OpenFolderCommand.RaiseCanExecuteChanged();
            RefreshCommands();
        }
    }

    public string DownloadFolder
    {
        get => _downloadFolder;
        set
        {
            if (SetProperty(ref _downloadFolder, value))
            {
                OnPropertyChanged(nameof(OutputFolder));
                RefreshCommands();
                OpenFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string OutputFolder => IsAudioOnly ? _musicLibrary.TracksFolder : DownloadFolder;
    public bool IsAudioOnly => SelectedFormat.AudioOnly;
    public double ProgressPercentage { get => _progressPercentage; private set { if (SetProperty(ref _progressPercentage, value)) OnPropertyChanged(nameof(PercentageText)); } }
    public string PercentageText => $"{ProgressPercentage:0.0}%";
    public string Speed { get => _speed; private set => SetProperty(ref _speed, value); }
    public string Eta { get => _eta; private set => SetProperty(ref _eta, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ErrorDetails { get => _errorDetails; private set { if (SetProperty(ref _errorDetails, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorDetails);
    public bool HasVideo => Video is not null;
    public bool IsBusy => IsAnalysing || IsDownloading;
    public bool IsNotBusy => !IsBusy;
    public bool IsComplete { get => _isComplete; private set => SetProperty(ref _isComplete, value); }
    public bool IsAnalysing { get => _isAnalysing; private set { if (SetProperty(ref _isAnalysing, value)) BusyStateChanged(); } }
    public bool IsDownloading { get => _isDownloading; private set { if (SetProperty(ref _isDownloading, value)) BusyStateChanged(); } }

    public void RefreshMusicDownloadFolder()
    {
        OnPropertyChanged(nameof(OutputFolder));
        OpenFolderCommand.RaiseCanExecuteChanged();
        RefreshCommands();
    }

    private bool CanAnalyse() => !IsBusy && !string.IsNullOrWhiteSpace(Url);
    private bool CanDownload() => !IsBusy && Video is not null && !string.IsNullOrWhiteSpace(OutputFolder);

    private async Task AnalyseAsync()
    {
        ErrorDetails = string.Empty;
        IsComplete = false;
        IsAnalysing = true;
        StatusText = "Analysing video…";
        Video = null;
        _analysisResult = null;
        try
        {
            var originalUrl = Url.Trim();
            if (PlaylistImport.IsPlaylistUrl(originalUrl))
            {
                _normalisedUrl = UrlCleaner.NormalizePlaylist(originalUrl);
                Url = _normalisedUrl;
                await PlaylistImport.LoadAsync(_normalisedUrl);
                StatusText = PlaylistImport.HasPlaylist ? "Playlist ready for selection." : PlaylistImport.StatusText;
                return;
            }

            PlaylistImport.Clear();
            _normalisedUrl = UrlCleaner.CleanSingleVideo(originalUrl);
            Url = _normalisedUrl;
            var result = await _ytDlpService.AnalyzeAsync(_normalisedUrl);
            if (!result.IsSuccess)
            {
                ShowError(new YtDlpException(result.DiagnosticMessage, result.RawStdErr));
                return;
            }

            _analysisResult = result;
            Video = result.VideoInfo;
            Debug.WriteLine($"Successful YouTube client strategy: {result.SuccessfulStrategy?.ToString() ?? "Normal yt-dlp behavior"}");
            StatusText = "Ready to download.";
        }
        catch (YtDlpException ex) { ShowError(ex); }
        catch (Exception ex) { ShowError(new YtDlpException("The video could not be analysed.", ex.Message, ex)); }
        finally { IsAnalysing = false; }
    }

    private async Task DownloadAsync()
    {
        ErrorDetails = string.Empty;
        IsComplete = false;
        IsDownloading = true;
        ProgressPercentage = 0;
        Speed = "—";
        Eta = "—";
        StatusText = IsAudioOnly ? "Downloading audio…" : "Starting download…";
        _downloadCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<DownloadProgress>(UpdateProgress);
            if (IsAudioOnly && Video is not null)
            {
                var audioResult = await _audioDownloader.DownloadAsync(
                    _normalisedUrl, Video, _analysisResult, progress, _downloadCancellation.Token);
                if (!audioResult.WasAlreadyInLibrary) AudioTrackAdded?.Invoke(this, audioResult.Track);
                StatusText = audioResult.WasAlreadyInLibrary ? "This track is already in the Music library." : "Audio added to the music library.";
            }
            else
            {
                await _settingsService.SaveDownloadFolderAsync(DownloadFolder);
                await _ytDlpService.DownloadAsync(
                    _normalisedUrl, SelectedFormat, OutputFolder, _analysisResult?.SuccessfulStrategy,
                    progress, _downloadCancellation.Token);
                StatusText = "Download complete.";
            }

            ProgressPercentage = 100;
            Speed = "—";
            Eta = "—";
            IsComplete = true;
            DownloadCompleted?.Invoke(this, EventArgs.Empty);
            OpenFolderCommand.RaiseCanExecuteChanged();
        }
        catch (OperationCanceledException) { StatusText = "Download cancelled."; }
        catch (YtDlpException ex) { ShowError(ex); }
        catch (Exception ex) { ShowError(new YtDlpException("The download failed.", ex.Message, ex)); }
        finally
        {
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
            IsDownloading = false;
        }
    }

    private async Task BrowseAsync()
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Choose video download folder",
                InitialDirectory = Directory.Exists(DownloadFolder) ? DownloadFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            if (dialog.ShowDialog() != true) return;
            DownloadFolder = dialog.FolderName;
            await _settingsService.SaveDownloadFolderAsync(DownloadFolder);
            StatusText = "Download folder updated.";
        }
        catch (Exception ex) { ShowError(new YtDlpException("The download folder could not be updated.", ex.Message, ex)); }
    }

    private void UpdateProgress(DownloadProgress update)
    {
        if (update.Percentage > 0 || update.StatusText.Contains("100%", StringComparison.OrdinalIgnoreCase)) ProgressPercentage = update.Percentage;
        if (update.Speed != "—") Speed = update.Speed;
        if (update.Eta != "—") Eta = update.Eta;
        StatusText = update.StatusText;
    }

    private void CancelDownload() => _downloadCancellation?.Cancel();
    private void OpenFolder()
    {
        Directory.CreateDirectory(OutputFolder);
        Process.Start(new ProcessStartInfo { FileName = OutputFolder, UseShellExecute = true });
    }
    private void ShowError(YtDlpException exception) { StatusText = exception.Message; ErrorDetails = exception.Details; }
    private void BusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsNotBusy));
        RefreshCommands();
        CancelCommand.RaiseCanExecuteChanged();
        BrowseCommand.RaiseCanExecuteChanged();
    }
    private void RefreshCommands()
    {
        AnalyseCommand.RaiseCanExecuteChanged();
        DownloadCommand.RaiseCanExecuteChanged();
    }
}
