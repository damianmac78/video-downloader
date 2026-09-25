using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DownloaderV2.ViewModels;

namespace DownloaderV2.Views;

public partial class VisualizerWindow : Window
{
    private readonly DispatcherTimer _overlayTimer;
    private bool _fullScreen;
    private WindowState _savedState;
    private WindowStyle _savedStyle;
    private ResizeMode _savedResizeMode;

    public VisualizerWindow()
    {
        InitializeComponent();
        _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _overlayTimer.Tick += (_, _) => HideOverlay();
        Loaded += (_, _) => RestartOverlayTimer();
        Closed += (_, _) => _overlayTimer.Stop();
    }

    private VisualizerWindowViewModel? ViewModel => DataContext as VisualizerWindowViewModel;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: ViewModel?.PreviousCommand.Execute(null); e.Handled = true; break;
            case Key.Right: ViewModel?.NextCommand.Execute(null); e.Handled = true; break;
            case Key.Space: ViewModel?.Player.PlayPauseCommand.Execute(null); e.Handled = true; break;
            case Key.F: ToggleFullScreen(); e.Handled = true; break;
            case Key.Escape when _fullScreen: ToggleFullScreen(); e.Handled = true; break;
        }
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ToggleFullScreen();
    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        ControlsOverlay.Visibility = Visibility.Visible;
        ShortcutHint.Visibility = Visibility.Visible;
        Cursor = Cursors.Arrow;
        RestartOverlayTimer();
    }

    private void RestartOverlayTimer()
    {
        _overlayTimer.Stop();
        _overlayTimer.Start();
    }

    private void HideOverlay()
    {
        if (!_fullScreen) return;
        ControlsOverlay.Visibility = Visibility.Collapsed;
        ShortcutHint.Visibility = Visibility.Collapsed;
        Cursor = Cursors.None;
        _overlayTimer.Stop();
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _savedState = WindowState;
            _savedStyle = WindowStyle;
            _savedResizeMode = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _fullScreen = true;
        }
        else
        {
            WindowState = _savedState == WindowState.Minimized ? WindowState.Normal : _savedState;
            WindowStyle = _savedStyle;
            ResizeMode = _savedResizeMode;
            Cursor = Cursors.Arrow;
            ControlsOverlay.Visibility = Visibility.Visible;
            ShortcutHint.Visibility = Visibility.Visible;
            _fullScreen = false;
        }
        RestartOverlayTimer();
    }
}
