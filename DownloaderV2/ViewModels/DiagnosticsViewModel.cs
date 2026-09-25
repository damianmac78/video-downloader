using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly DiagnosticsService _diagnostics;
    private DiagnosticsSnapshot? _snapshot;
    private string _statusText = "Loading diagnostics…";
    private string _technicalDetails = string.Empty;
    private bool _isBusy;

    public DiagnosticsViewModel(DiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        UpdateYtDlpCommand = new AsyncCommand(UpdateYtDlpAsync, () => !IsBusy);
    }

    public DiagnosticsSnapshot? Snapshot { get => _snapshot; private set => SetProperty(ref _snapshot, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string TechnicalDetails { get => _technicalDetails; private set => SetProperty(ref _technicalDetails, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RefreshCommands(); } }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand UpdateYtDlpCommand { get; }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Snapshot = await _diagnostics.GetSnapshotAsync();
            StatusText = "Diagnostics refreshed.";
        }
        catch (Exception ex)
        {
            StatusText = "Diagnostics could not be loaded.";
            TechnicalDetails = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task UpdateYtDlpAsync()
    {
        IsBusy = true;
        StatusText = "Checking for a yt-dlp update…";
        TechnicalDetails = string.Empty;
        try
        {
            var result = await _diagnostics.UpdateYtDlpAsync();
            StatusText = result.Message;
            TechnicalDetails = result.Details;
            Snapshot = await _diagnostics.GetSnapshotAsync();
        }
        finally { IsBusy = false; }
    }

    private void RefreshCommands()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        UpdateYtDlpCommand.RaiseCanExecuteChanged();
    }
}
