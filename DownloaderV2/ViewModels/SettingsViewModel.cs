using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using DownloaderV2.Models;
using DownloaderV2.Services;
using Microsoft.Win32;

namespace DownloaderV2.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ILastFmService _lastFm;
    private readonly MusicLibraryService _musicLibrary;
    private readonly RadioCacheService _radioCache;
    private string _apiKey;
    private string _statusText;
    private bool _isBusy;
    private int _radioPreloadCount;
    private int _radioCacheMaxAgeDays;
    private double _radioCacheMaxSizeGb;
    private string _musicScanFolder;
    private string _musicScanStatus = "Choose a folder to add or reconnect music files.";
    private string _musicDownloadFolder;
    private string _musicDownloadStatus = "New music downloads are saved here.";
    private string _radioCacheFolder;
    private string _radioCacheStatus = "Temporary Radio tracks are saved here.";
    private Track? _selectedCachedTrack;
    private string _radioCacheSearch = string.Empty;
    private string _radioCacheBrowserStatus = "Cached Radio tracks remain available until automatic cleanup.";

    public SettingsViewModel(SettingsService settings, ILastFmService lastFm, MusicLibraryService musicLibrary, RadioCacheService radioCache)
    {
        _settings = settings;
        _lastFm = lastFm;
        _musicLibrary = musicLibrary;
        _radioCache = radioCache;
        _apiKey = settings.GetUserLastFmApiKey() ?? string.Empty;
        _statusText = lastFm.IsConfigured ? "Configured — test the connection to verify the key." : "Not configured";
        _radioPreloadCount = settings.GetRadioPreloadCount();
        _radioCacheMaxAgeDays = settings.GetRadioCacheMaxAgeDays();
        _radioCacheMaxSizeGb = settings.GetRadioCacheMaxSizeGb();
        _musicScanFolder = musicLibrary.LibraryRoot;
        _musicDownloadFolder = musicLibrary.TracksFolder;
        _radioCacheFolder = radioCache.RootPath;
        SaveCommand = new AsyncCommand(SaveAsync, () => !IsBusy);
        TestConnectionCommand = new AsyncCommand(TestConnectionAsync, () => !IsBusy);
        OpenApiKeyPageCommand = new RelayCommand(OpenApiKeyPage);
        SaveRadioCommand = new AsyncCommand(SaveRadioAsync, () => !IsBusy);
        BrowseMusicFolderCommand = new RelayCommand(BrowseMusicFolder, () => !IsBusy);
        ScanMusicFolderCommand = new AsyncCommand(ScanMusicFolderAsync, () => !IsBusy && Directory.Exists(MusicScanFolder));
        BrowseMusicDownloadFolderCommand = new RelayCommand(BrowseMusicDownloadFolder, () => !IsBusy);
        SaveMusicDownloadFolderCommand = new AsyncCommand(SaveMusicDownloadFolderAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(MusicDownloadFolder));
        BrowseRadioCacheFolderCommand = new RelayCommand(BrowseRadioCacheFolder, () => !IsBusy);
        SaveRadioCacheFolderCommand = new AsyncCommand(SaveRadioCacheFolderAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(RadioCacheFolder));
        RefreshRadioCacheCommand = new AsyncCommand(RefreshRadioCacheAsync, () => !IsBusy);
        AddCachedTrackCommand = new AsyncCommand(AddCachedTrackAsync, () => !IsBusy && SelectedCachedTrack is not null);
        RadioCacheView = CollectionViewSource.GetDefaultView(RadioCacheTracks);
        RadioCacheView.Filter = FilterCachedTrack;
    }

    public AsyncCommand SaveCommand { get; }
    public AsyncCommand TestConnectionCommand { get; }
    public RelayCommand OpenApiKeyPageCommand { get; }
    public AsyncCommand SaveRadioCommand { get; }
    public RelayCommand BrowseMusicFolderCommand { get; }
    public AsyncCommand ScanMusicFolderCommand { get; }
    public RelayCommand BrowseMusicDownloadFolderCommand { get; }
    public AsyncCommand SaveMusicDownloadFolderCommand { get; }
    public RelayCommand BrowseRadioCacheFolderCommand { get; }
    public AsyncCommand SaveRadioCacheFolderCommand { get; }
    public AsyncCommand RefreshRadioCacheCommand { get; }
    public AsyncCommand AddCachedTrackCommand { get; }
    public ObservableCollection<Track> RadioCacheTracks { get; } = [];
    public ICollectionView RadioCacheView { get; }
    public event EventHandler? MusicLibraryChanged;
    public event EventHandler? DownloadFoldersChanged;
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            SaveCommand.RaiseCanExecuteChanged();
            TestConnectionCommand.RaiseCanExecuteChanged();
            SaveRadioCommand.RaiseCanExecuteChanged();
            BrowseMusicFolderCommand.RaiseCanExecuteChanged();
            ScanMusicFolderCommand.RaiseCanExecuteChanged();
            BrowseMusicDownloadFolderCommand.RaiseCanExecuteChanged();
            SaveMusicDownloadFolderCommand.RaiseCanExecuteChanged();
            BrowseRadioCacheFolderCommand.RaiseCanExecuteChanged();
            SaveRadioCacheFolderCommand.RaiseCanExecuteChanged();
            RefreshRadioCacheCommand.RaiseCanExecuteChanged();
            AddCachedTrackCommand.RaiseCanExecuteChanged();
        }
    }
    public int RadioPreloadCount { get => _radioPreloadCount; set => SetProperty(ref _radioPreloadCount, value); }
    public int RadioCacheMaxAgeDays { get => _radioCacheMaxAgeDays; set => SetProperty(ref _radioCacheMaxAgeDays, value); }
    public double RadioCacheMaxSizeGb { get => _radioCacheMaxSizeGb; set => SetProperty(ref _radioCacheMaxSizeGb, value); }
    public string MusicScanFolder
    {
        get => _musicScanFolder;
        set
        {
            if (!SetProperty(ref _musicScanFolder, value)) return;
            ScanMusicFolderCommand.RaiseCanExecuteChanged();
        }
    }
    public string MusicScanStatus { get => _musicScanStatus; private set => SetProperty(ref _musicScanStatus, value); }
    public string MusicDownloadFolder
    {
        get => _musicDownloadFolder;
        set
        {
            if (!SetProperty(ref _musicDownloadFolder, value)) return;
            SaveMusicDownloadFolderCommand.RaiseCanExecuteChanged();
        }
    }
    public string MusicDownloadStatus { get => _musicDownloadStatus; private set => SetProperty(ref _musicDownloadStatus, value); }
    public string RadioCacheFolder
    {
        get => _radioCacheFolder;
        set
        {
            if (!SetProperty(ref _radioCacheFolder, value)) return;
            SaveRadioCacheFolderCommand.RaiseCanExecuteChanged();
        }
    }
    public string RadioCacheStatus { get => _radioCacheStatus; private set => SetProperty(ref _radioCacheStatus, value); }
    public Track? SelectedCachedTrack
    {
        get => _selectedCachedTrack;
        set
        {
            if (!SetProperty(ref _selectedCachedTrack, value)) return;
            AddCachedTrackCommand.RaiseCanExecuteChanged();
        }
    }
    public string RadioCacheSearch
    {
        get => _radioCacheSearch;
        set { if (SetProperty(ref _radioCacheSearch, value)) RadioCacheView.Refresh(); }
    }
    public string RadioCacheBrowserStatus { get => _radioCacheBrowserStatus; private set => SetProperty(ref _radioCacheBrowserStatus, value); }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await _settings.SaveLastFmApiKeyAsync(ApiKey);
            StatusText = string.IsNullOrWhiteSpace(ApiKey) ? "Not configured" : "Saved — test the connection to verify the key.";
        }
        finally { IsBusy = false; }
    }

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        StatusText = "Testing connection…";
        try
        {
            await _settings.SaveLastFmApiKeyAsync(ApiKey);
            await _lastFm.TestConnectionAsync();
            StatusText = "Connected";
        }
        catch (LastFmException ex)
        {
            StatusText = ex.Kind switch
            {
                LastFmFailureKind.NotConfigured => "Not configured",
                LastFmFailureKind.InvalidApiKey => "Invalid API key",
                LastFmFailureKind.Network or LastFmFailureKind.RateLimited => "Network error",
                _ => "Last.fm returned an unexpected response"
            };
        }
        finally { IsBusy = false; }
    }

    private static void OpenApiKeyPage() => Process.Start(new ProcessStartInfo
    {
        FileName = "https://www.last.fm/api/account/create",
        UseShellExecute = true
    });

    private async Task SaveRadioAsync()
    {
        IsBusy = true;
        try
        {
            await _settings.SaveRadioSettingsAsync(RadioPreloadCount, RadioCacheMaxAgeDays, RadioCacheMaxSizeGb);
            RadioPreloadCount = _settings.GetRadioPreloadCount();
            RadioCacheMaxAgeDays = _settings.GetRadioCacheMaxAgeDays();
            RadioCacheMaxSizeGb = _settings.GetRadioCacheMaxSizeGb();
        }
        finally { IsBusy = false; }
    }

    private void BrowseMusicFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing music",
            InitialDirectory = Directory.Exists(MusicScanFolder) ? MusicScanFolder : _musicLibrary.LibraryRoot
        };
        if (dialog.ShowDialog() == true) MusicScanFolder = dialog.FolderName;
    }

    private async Task ScanMusicFolderAsync()
    {
        IsBusy = true;
        MusicScanStatus = "Scanning music files…";
        try
        {
            var result = await _musicLibrary.ScanFolderAsync(MusicScanFolder);
            MusicScanStatus = $"Found {result.FilesFound}; added {result.Added}, reconnected {result.Relinked}, already present {result.Unchanged}" +
                              (result.Failed > 0 ? $", failed {result.Failed}." : ".");
            MusicLibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MusicScanStatus = $"Scan failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private void BrowseMusicDownloadFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the default folder for new music downloads",
            InitialDirectory = Directory.Exists(MusicDownloadFolder) ? MusicDownloadFolder : _musicLibrary.TracksFolder
        };
        if (dialog.ShowDialog() == true) MusicDownloadFolder = dialog.FolderName;
    }

    private async Task SaveMusicDownloadFolderAsync()
    {
        IsBusy = true;
        try
        {
            _musicLibrary.SetTracksFolder(MusicDownloadFolder);
            MusicDownloadFolder = _musicLibrary.TracksFolder;
            await _settings.SaveMusicDownloadFolderAsync(MusicDownloadFolder);
            MusicDownloadStatus = "Default music download folder updated. Existing files were not moved.";
            DownloadFoldersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { MusicDownloadStatus = $"Could not update folder: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private void BrowseRadioCacheFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the Radio cache folder",
            InitialDirectory = Directory.Exists(RadioCacheFolder) ? RadioCacheFolder : _radioCache.RootPath
        };
        if (dialog.ShowDialog() == true) RadioCacheFolder = dialog.FolderName;
    }

    private async Task SaveRadioCacheFolderAsync()
    {
        IsBusy = true;
        try
        {
            _radioCache.SetRootPath(RadioCacheFolder);
            RadioCacheFolder = _radioCache.RootPath;
            await _settings.SaveRadioCacheFolderAsync(RadioCacheFolder);
            RadioCacheStatus = "Radio cache folder updated. Existing cache files were not moved.";
            await RefreshRadioCacheAsync();
            DownloadFoldersChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { RadioCacheStatus = $"Could not update folder: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public async Task RefreshRadioCacheAsync()
    {
        var selectedPath = SelectedCachedTrack?.FilePath;
        RadioCacheTracks.Clear();
        foreach (var track in await _radioCache.GetCachedTracksAsync()) RadioCacheTracks.Add(track);
        SelectedCachedTrack = RadioCacheTracks.FirstOrDefault(track =>
            string.Equals(track.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
        RadioCacheView.Refresh();
        RadioCacheBrowserStatus = $"{RadioCacheTracks.Count} cached Radio track{(RadioCacheTracks.Count == 1 ? string.Empty : "s")}.";
    }

    private async Task AddCachedTrackAsync()
    {
        if (SelectedCachedTrack is null) return;
        IsBusy = true;
        var cached = SelectedCachedTrack;
        try
        {
            var kept = await _radioCache.KeepAsync(cached);
            RadioCacheTracks.Remove(cached);
            SelectedCachedTrack = null;
            RadioCacheBrowserStatus = $"Added {kept.Artist} — {kept.Title} to your music library.";
            MusicLibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { RadioCacheBrowserStatus = $"Could not add cached track: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private bool FilterCachedTrack(object item)
    {
        if (item is not Track track || string.IsNullOrWhiteSpace(RadioCacheSearch)) return true;
        return track.Title.Contains(RadioCacheSearch, StringComparison.OrdinalIgnoreCase) ||
               track.Artist.Contains(RadioCacheSearch, StringComparison.OrdinalIgnoreCase);
    }
}
