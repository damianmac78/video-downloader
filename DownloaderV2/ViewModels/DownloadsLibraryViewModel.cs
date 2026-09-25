using System.Collections.ObjectModel;
using System.Diagnostics;
using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class DownloadsLibraryViewModel : ObservableObject
{
    private readonly DownloadLibraryService _library;
    private DownloadedMediaItem? _selectedItem;
    private string _statusText = string.Empty;

    public DownloadsLibraryViewModel(DownloadLibraryService library)
    {
        _library = library;
        RefreshCommand = new AsyncCommand(RefreshAsync);
        OpenCommand = new RelayCommand(OpenSelected, () => SelectedItem is not null);
        OpenFolderCommand = new RelayCommand(OpenSelectedFolder, () => SelectedItem is not null);
    }

    public ObservableCollection<DownloadedMediaItem> Items { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public DownloadedMediaItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            OpenCommand.RaiseCanExecuteChanged();
            OpenFolderCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            var items = await _library.GetDownloadsAsync();
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            StatusText = $"{Items.Count} downloaded media file{(Items.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            StatusText = $"The download library could not be refreshed: {ex.Message}";
        }
    }

    private void OpenSelected()
    {
        if (SelectedItem is null) return;
        if (!File.Exists(SelectedItem.FilePath)) { StatusText = "That downloaded file no longer exists."; return; }
        Process.Start(new ProcessStartInfo { FileName = SelectedItem.FilePath, UseShellExecute = true });
    }

    private void OpenSelectedFolder()
    {
        if (SelectedItem is null) return;
        if (!Directory.Exists(SelectedItem.Folder)) { StatusText = "That folder no longer exists."; return; }
        Process.Start(new ProcessStartInfo { FileName = SelectedItem.Folder, UseShellExecute = true });
    }
}
