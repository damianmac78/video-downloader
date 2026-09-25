using DownloaderV2.ViewModels;
using DownloaderV2.Views;

namespace DownloaderV2.Services;

public sealed class VisualizerWindowService(PlayerViewModel player, SettingsService settings) : IDisposable
{
    private VisualizerWindow? _window;

    public void Show()
    {
        if (_window is not null)
        {
            if (_window.WindowState == System.Windows.WindowState.Minimized) _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
            return;
        }

        _window = new VisualizerWindow
        {
            DataContext = new VisualizerWindowViewModel(player, settings),
            Owner = System.Windows.Application.Current.MainWindow
        };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
    }
}
