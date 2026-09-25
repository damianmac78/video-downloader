namespace DownloaderV2.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private object _currentPage;
    private string _activeSection = "Download";

    public MainViewModel(DownloadViewModel download, LibraryViewModel library, PlayerViewModel player)
    {
        Download = download;
        Library = library;
        Player = player;
        _currentPage = download;
        ShowDownloadCommand = new RelayCommand(() => Navigate("Download", Download));
        ShowLibraryCommand = new RelayCommand(() => Navigate("Library", Library));
        Download.AudioTrackAdded += async (_, _) => await Library.RefreshAsync();
    }

    public DownloadViewModel Download { get; }
    public LibraryViewModel Library { get; }
    public PlayerViewModel Player { get; }
    public RelayCommand ShowDownloadCommand { get; }
    public RelayCommand ShowLibraryCommand { get; }
    public object CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public string ActiveSection { get => _activeSection; private set => SetProperty(ref _activeSection, value); }

    private void Navigate(string section, object page)
    {
        ActiveSection = section;
        CurrentPage = page;
    }
}
