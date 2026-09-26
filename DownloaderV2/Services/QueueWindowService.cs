using DownloaderV2.ViewModels;
using DownloaderV2.Views;

namespace DownloaderV2.Services;

public sealed class QueueWindowService(QueueViewModel viewModel) : IDisposable
{
    private QueueWindow? _window;
    public void Show()
    {
        viewModel.Refresh();
        if (_window is null || !_window.IsLoaded)
        {
            var window = new QueueWindow { DataContext = viewModel, Owner = System.Windows.Application.Current.MainWindow };
            _window = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window)) _window = null;
            };
            window.Show();
        }
        else
        {
            if (!_window.IsVisible) _window.Show();
            if (_window.WindowState == System.Windows.WindowState.Minimized)
                _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            _window.Focus();
        }
    }
    public void Dispose() { _window?.Close(); _window = null; viewModel.Dispose(); }
}
