using System.Diagnostics;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ILastFmService _lastFm;
    private string _apiKey;
    private string _statusText;
    private bool _isBusy;
    private int _radioPreloadCount;
    private int _radioCacheMaxAgeDays;
    private double _radioCacheMaxSizeGb;

    public SettingsViewModel(SettingsService settings, ILastFmService lastFm)
    {
        _settings = settings;
        _lastFm = lastFm;
        _apiKey = settings.GetUserLastFmApiKey() ?? string.Empty;
        _statusText = lastFm.IsConfigured ? "Configured — test the connection to verify the key." : "Not configured";
        _radioPreloadCount = settings.GetRadioPreloadCount();
        _radioCacheMaxAgeDays = settings.GetRadioCacheMaxAgeDays();
        _radioCacheMaxSizeGb = settings.GetRadioCacheMaxSizeGb();
        SaveCommand = new AsyncCommand(SaveAsync, () => !IsBusy);
        TestConnectionCommand = new AsyncCommand(TestConnectionAsync, () => !IsBusy);
        OpenApiKeyPageCommand = new RelayCommand(OpenApiKeyPage);
        SaveRadioCommand = new AsyncCommand(SaveRadioAsync, () => !IsBusy);
    }

    public AsyncCommand SaveCommand { get; }
    public AsyncCommand TestConnectionCommand { get; }
    public RelayCommand OpenApiKeyPageCommand { get; }
    public AsyncCommand SaveRadioCommand { get; }
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            SaveCommand.RaiseCanExecuteChanged();
            TestConnectionCommand.RaiseCanExecuteChanged();
            SaveRadioCommand.RaiseCanExecuteChanged();
        }
    }
    public int RadioPreloadCount { get => _radioPreloadCount; set => SetProperty(ref _radioPreloadCount, value); }
    public int RadioCacheMaxAgeDays { get => _radioCacheMaxAgeDays; set => SetProperty(ref _radioCacheMaxAgeDays, value); }
    public double RadioCacheMaxSizeGb { get => _radioCacheMaxSizeGb; set => SetProperty(ref _radioCacheMaxSizeGb, value); }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await _settings.SaveLastFmApiKeyAsync(ApiKey);
            StatusText = string.IsNullOrWhiteSpace(ApiKey) ? "Not configured" : "Saved — test the connection to verify the key.";
        }
        finally { IsBusy = false; }
    }

    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        StatusText = "Testing connection…";
        try
        {
            await _settings.SaveLastFmApiKeyAsync(ApiKey);
            await _lastFm.TestConnectionAsync();
            StatusText = "Connected";
        }
        catch (LastFmException ex)
        {
            StatusText = ex.Kind switch
            {
                LastFmFailureKind.NotConfigured => "Not configured",
                LastFmFailureKind.InvalidApiKey => "Invalid API key",
                LastFmFailureKind.Network or LastFmFailureKind.RateLimited => "Network error",
                _ => "Last.fm returned an unexpected response"
            };
        }
        finally { IsBusy = false; }
    }

    private static void OpenApiKeyPage() => Process.Start(new ProcessStartInfo
    {
        FileName = "https://www.last.fm/api/account/create",
        UseShellExecute = true
    });

    private async Task SaveRadioAsync()
    {
        IsBusy = true;
        try
        {
            await _settings.SaveRadioSettingsAsync(RadioPreloadCount, RadioCacheMaxAgeDays, RadioCacheMaxSizeGb);
            RadioPreloadCount = _settings.GetRadioPreloadCount();
            RadioCacheMaxAgeDays = _settings.GetRadioCacheMaxAgeDays();
            RadioCacheMaxSizeGb = _settings.GetRadioCacheMaxSizeGb();
        }
        finally { IsBusy = false; }
    }
}
