using DownloaderV2.ViewModels;
using DownloaderV2.Views;

namespace DownloaderV2.Services;

public sealed class DiagnosticsWindowService(DiagnosticsService diagnostics) : IDisposable
{
    private DiagnosticsWindow? _window;

    public void Show()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var viewModel = new DiagnosticsViewModel(diagnostics);
        _window = new DiagnosticsWindow
        {
            DataContext = viewModel,
            Owner = System.Windows.Application.Current.MainWindow
        };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        _ = viewModel.RefreshAsync();
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
    }
}
