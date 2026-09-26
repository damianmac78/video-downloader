using System.Windows;
using DownloaderV2.Data;
using DownloaderV2.Services;
using DownloaderV2.ViewModels;
using DownloaderV2.Views;

namespace DownloaderV2;

public partial class App : Application
{
    private AudioPlayerService? _audioPlayer;
    private PlayerViewModel? _playerViewModel;
    private VisualizerWindowService? _visualizerWindowService;
    private DiagnosticsWindowService? _diagnosticsWindowService;
    private DiscoveryWindowService? _discoveryWindowService;
    private MainViewModel? _mainViewModel;
    private LibraryViewModel? _libraryViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var settings = new SettingsService();
            var warnings = new List<string>();
            var libraryRoot = EnsureStorageWithFallback(
                settings.GetMusicLibraryFolder(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloaderV2", "MusicLibrary"),
                "music library folder", warnings);
            var downloadFolder = EnsureStorageWithFallback(
                settings.GetDownloadFolder(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloaderV2", "Downloads"),
                "download folder", warnings);
            if (!string.Equals(libraryRoot, settings.GetMusicLibraryFolder(), StringComparison.OrdinalIgnoreCase))
                await settings.SaveMusicLibraryFolderAsync(libraryRoot);
            if (!string.Equals(downloadFolder, settings.GetDownloadFolder(), StringComparison.OrdinalIgnoreCase))
                await settings.SaveDownloadFolderAsync(downloadFolder);

            var database = new AppDbContext(Path.Combine(libraryRoot, "library.db"));
            await database.InitializeAsync();

            var musicLibrary = new MusicLibraryService(database, libraryRoot);
            musicLibrary.EnsureFolders();
            StorageService.EnsureWritableDirectory(musicLibrary.TracksFolder, "music tracks folder");
            StorageService.EnsureWritableDirectory(musicLibrary.ArtworkFolder, "artwork cache folder");
            var playlistService = new PlaylistService(database);
            var artwork = new ArtworkService(musicLibrary.ArtworkFolder);
            _audioPlayer = new AudioPlayerService();
            _playerViewModel = new PlayerViewModel(_audioPlayer);
            _visualizerWindowService = new VisualizerWindowService(_playerViewModel, settings);
            _playerViewModel.OpenVisualizerRequested += PlayerViewModelOnOpenVisualizerRequested;
            var playlistViewModel = new PlaylistViewModel(playlistService, _playerViewModel);
            _libraryViewModel = new LibraryViewModel(musicLibrary, _playerViewModel, playlistViewModel);
            await _libraryViewModel.InitializeAsync();
            var ytDlp = new YtDlpService();
            var audioDownloader = new AudioLibraryDownloadService(ytDlp, musicLibrary, artwork);
            var playlistImportViewModel = new PlaylistImportViewModel(new PlaylistImportService(ytDlp), audioDownloader, playlistService);
            var downloadViewModel = new DownloadViewModel(ytDlp, settings, musicLibrary, audioDownloader, playlistImportViewModel);
            var downloadsLibraryViewModel = new DownloadsLibraryViewModel(new DownloadLibraryService(settings, musicLibrary));
            await downloadsLibraryViewModel.RefreshAsync();
            var lastFm = new LastFmService(settings);
            var settingsViewModel = new SettingsViewModel(settings, lastFm);
            var discoveryViewModel = new DiscoveryViewModel(new MusicDiscoveryService(lastFm, ytDlp, musicLibrary), audioDownloader, playlistViewModel, _playerViewModel);
            discoveryViewModel.TrackAdded += async (_, _) => { await _libraryViewModel.RefreshAsync(); await downloadsLibraryViewModel.RefreshAsync(); };
            _discoveryWindowService = new DiscoveryWindowService(discoveryViewModel);
            _libraryViewModel.DiscoverRequested += LibraryViewModelOnDiscoverRequested;
            var diagnostics = new DiagnosticsService(settings, database.DatabasePath, libraryRoot);
            _diagnosticsWindowService = new DiagnosticsWindowService(diagnostics);

            var window = new MainWindow
            {
                DataContext = _mainViewModel = new MainViewModel(downloadViewModel, _libraryViewModel, downloadsLibraryViewModel, settingsViewModel, _playerViewModel)
            };
            _mainViewModel.DiagnosticsRequested += MainViewModelOnDiagnosticsRequested;
            MainWindow = window;
            window.Show();

            var missingTools = diagnostics.GetMissingTools();
            if (missingTools.Count > 0)
                warnings.Add($"Missing bundled tools: {string.Join(", ", missingTools)}. Copy them into:\n{diagnostics.ToolsPath}\n\nDownloader features will remain unavailable until they are supplied.");
            if (warnings.Count > 0)
                MessageBox.Show(window, string.Join("\n\n", warnings), "DownloaderV2 setup notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"DownloaderV2 could not start.\n\n{ex.Message}", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainViewModel is not null) _mainViewModel.DiagnosticsRequested -= MainViewModelOnDiagnosticsRequested;
        if (_libraryViewModel is not null) _libraryViewModel.DiscoverRequested -= LibraryViewModelOnDiscoverRequested;
        if (_playerViewModel is not null) _playerViewModel.OpenVisualizerRequested -= PlayerViewModelOnOpenVisualizerRequested;
        _diagnosticsWindowService?.Dispose();
        _discoveryWindowService?.Dispose();
        _visualizerWindowService?.Dispose();
        _playerViewModel?.Dispose();
        _audioPlayer?.Dispose();
        base.OnExit(e);
    }

    private void PlayerViewModelOnOpenVisualizerRequested(object? sender, EventArgs e) => _visualizerWindowService?.Show();
    private void MainViewModelOnDiagnosticsRequested(object? sender, EventArgs e) => _diagnosticsWindowService?.Show();
    private void LibraryViewModelOnDiscoverRequested(object? sender, Models.Track track) => _discoveryWindowService?.Show(track);

    private static string EnsureStorageWithFallback(string preferred, string fallback, string description, ICollection<string> warnings)
    {
        try
        {
            StorageService.EnsureWritableDirectory(preferred, description);
            return preferred;
        }
        catch (IOException ex)
        {
            StorageService.EnsureWritableDirectory(fallback, $"fallback {description}");
            warnings.Add($"{ex.Message}\nUsing: {fallback}");
            return fallback;
        }
    }
}
