using DownloaderV2.Models;
using DownloaderV2.ViewModels;
using DownloaderV2.Views;

namespace DownloaderV2.Services;

public sealed class DiscoveryWindowService(DiscoveryViewModel viewModel) : IDisposable
{
    private DiscoveryWindow? _window;

    public void Show(Track track)
    {
        if (_window is null)
        {
            _window = new DiscoveryWindow { DataContext = viewModel, Owner = System.Windows.Application.Current.MainWindow };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else _window.Activate();
        _ = viewModel.LoadAsync(track);
    }

    public void Dispose() { _window?.Close(); _window = null; }
}
