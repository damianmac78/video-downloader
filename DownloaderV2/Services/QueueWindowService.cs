using DownloaderV2.ViewModels;
using DownloaderV2.Views;

namespace DownloaderV2.Services;

public sealed class QueueWindowService(QueueViewModel viewModel) : IDisposable
{
    private QueueWindow? _window;
    public void Show()
    {
        if (_window is null)
        {
            _window = new QueueWindow { DataContext = viewModel, Owner = System.Windows.Application.Current.MainWindow };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else _window.Activate();
    }
    public void Dispose() { _window?.Close(); _window = null; viewModel.Dispose(); }
}
