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
    private readonly ArtworkService _artwork;
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
        ArtworkService artwork)
    {
        _ytDlpService = ytDlpService;
        _settingsService = settingsService;
        _musicLibrary = musicLibrary;
        _artwork = artwork;
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
            _normalisedUrl = UrlCleaner.Clean(Url);
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
        var started = DateTime.UtcNow.AddSeconds(-2);

        try
        {
            if (!IsAudioOnly) await _settingsService.SaveDownloadFolderAsync(DownloadFolder);
            var progress = new Progress<DownloadProgress>(UpdateProgress);
            var result = await _ytDlpService.DownloadAsync(
                _normalisedUrl,
                SelectedFormat,
                OutputFolder,
                _analysisResult?.SuccessfulStrategy,
                progress,
                _downloadCancellation.Token);

            if (IsAudioOnly && Video is not null)
            {
                var filePath = ResolveOutputFile(result.OutputFilePath, OutputFolder, started)
                    ?? throw new YtDlpException("The audio downloaded, but its output file could not be located.");
                var artworkPath = await _artwork.CacheAsync(Video.SourceVideoId, Video.ThumbnailUrl, _downloadCancellation.Token);
                var track = await _musicLibrary.AddOrUpdateTrackAsync(new Track
                {
                    Title = Video.Title,
                    Artist = Video.Uploader,
                    FilePath = filePath,
                    ArtworkPath = artworkPath,
                    DurationSeconds = Video.Duration?.TotalSeconds ?? 0,
                    SourceUrl = Video.SourceUrl ?? _normalisedUrl,
                    SourceVideoId = Video.SourceVideoId,
                    DateAdded = DateTime.UtcNow
                }, _downloadCancellation.Token);
                AudioTrackAdded?.Invoke(this, track);
                StatusText = "Audio added to the music library.";
            }
            else
            {
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

    private static string? ResolveOutputFile(string? reportedPath, string folder, DateTime started)
    {
        if (!string.IsNullOrWhiteSpace(reportedPath) && File.Exists(reportedPath)) return reportedPath;
        if (!Directory.Exists(folder)) return null;
        var mediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".m4a", ".mp3", ".aac", ".opus", ".ogg", ".webm", ".wav", ".flac" };
        return Directory.EnumerateFiles(folder)
            .Where(path => mediaExtensions.Contains(Path.GetExtension(path)) && File.GetLastWriteTimeUtc(path) >= started)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
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
