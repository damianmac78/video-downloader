namespace DownloaderV2.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private object _currentPage;
    private string _activeSection = "Download";

    public MainViewModel(DownloadViewModel download, LibraryViewModel music, DownloadsLibraryViewModel library, SettingsViewModel settings, PlayerViewModel player)
    {
        Download = download;
        Music = music;
        Library = library;
        Settings = settings;
        Player = player;
        _currentPage = download;
        ShowDownloadCommand = new RelayCommand(() => Navigate("Download", Download));
        ShowMusicCommand = new RelayCommand(() => Navigate("Music", Music));
        ShowLibraryCommand = new RelayCommand(() => Navigate("Library", Library));
        ShowSettingsCommand = new RelayCommand(() => Navigate("Settings", Settings));
        ShowDiagnosticsCommand = new RelayCommand(() => DiagnosticsRequested?.Invoke(this, EventArgs.Empty));
        Download.AudioTrackAdded += async (_, _) => await Music.RefreshAsync();
        Download.DownloadCompleted += async (_, _) =>
        {
            await Library.RefreshAsync();
            await Music.Playlists.InitializeAsync();
        };
    }

    public DownloadViewModel Download { get; }
    public LibraryViewModel Music { get; }
    public DownloadsLibraryViewModel Library { get; }
    public SettingsViewModel Settings { get; }
    public PlayerViewModel Player { get; }
    public RelayCommand ShowDownloadCommand { get; }
    public RelayCommand ShowMusicCommand { get; }
    public RelayCommand ShowLibraryCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ShowDiagnosticsCommand { get; }
    public event EventHandler? DiagnosticsRequested;
    public object CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public string ActiveSection { get => _activeSection; private set => SetProperty(ref _activeSection, value); }

    private void Navigate(string section, object page)
    {
        ActiveSection = section;
        CurrentPage = page;
    }
}
