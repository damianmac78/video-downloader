using DownloaderV2.Services;
using DownloaderV2.Visualizers;

namespace DownloaderV2.ViewModels;

public sealed class VisualizerWindowViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private string _selectedVisualizer;
    private int _saveVersion;

    public VisualizerWindowViewModel(PlayerViewModel player, SettingsService settings)
    {
        Player = player;
        _settings = settings;
        VisualizerNames = VisualizerCatalog.Names;
        var saved = settings.GetPreferredVisualizer();
        _selectedVisualizer = VisualizerNames.Contains(saved) ? saved : VisualizerCatalog.DefaultName;
        PreviousCommand = new RelayCommand(() => Move(-1));
        NextCommand = new RelayCommand(() => Move(1));
    }

    public PlayerViewModel Player { get; }
    public IReadOnlyList<string> VisualizerNames { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }

    public string SelectedVisualizer
    {
        get => _selectedVisualizer;
        set
        {
            if (!VisualizerNames.Contains(value) || !SetProperty(ref _selectedVisualizer, value)) return;
            _ = PersistSelectionAsync(value);
        }
    }

    private void Move(int offset)
    {
        var index = 0;
        for (var candidate = 0; candidate < VisualizerNames.Count; candidate++)
            if (VisualizerNames[candidate] == SelectedVisualizer) { index = candidate; break; }
        SelectedVisualizer = VisualizerNames[(index + offset + VisualizerNames.Count) % VisualizerNames.Count];
    }

    private async Task PersistSelectionAsync(string value)
    {
        var version = Interlocked.Increment(ref _saveVersion);
        try
        {
            await Task.Delay(250);
            if (version != _saveVersion) return;
            await _settings.SavePreferredVisualizerAsync(value);
        }
        catch { /* Preference persistence must never interrupt playback. */ }
    }
}
