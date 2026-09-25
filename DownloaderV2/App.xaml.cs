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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var settings = new SettingsService();
            var libraryRoot = settings.GetMusicLibraryFolder();
            var database = new AppDbContext(Path.Combine(libraryRoot, "library.db"));
            await database.InitializeAsync();

            var musicLibrary = new MusicLibraryService(database, libraryRoot);
            musicLibrary.EnsureFolders();
            var playlistService = new PlaylistService(database);
            var artwork = new ArtworkService(musicLibrary.ArtworkFolder);
            _audioPlayer = new AudioPlayerService();
            _playerViewModel = new PlayerViewModel(_audioPlayer);
            var playlistViewModel = new PlaylistViewModel(playlistService, _playerViewModel);
            var libraryViewModel = new LibraryViewModel(musicLibrary, _playerViewModel, playlistViewModel);
            await libraryViewModel.InitializeAsync();
            var downloadViewModel = new DownloadViewModel(new YtDlpService(), settings, musicLibrary, artwork);

            var window = new MainWindow
            {
                DataContext = new MainViewModel(downloadViewModel, libraryViewModel, _playerViewModel)
            };
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"DownloaderV2 could not start.\n\n{ex.Message}", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _playerViewModel?.Dispose();
        _audioPlayer?.Dispose();
        base.OnExit(e);
    }
}
