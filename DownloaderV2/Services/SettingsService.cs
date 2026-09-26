using System.Text.Json;
using System.Text.Json.Nodes;

namespace DownloaderV2.Services;

public sealed class SettingsService
{
    private readonly string _defaultsPath;
    private readonly string _settingsPath;
    private readonly string _developmentPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SettingsService(string? defaultsPath = null, string? settingsPath = null, string? developmentPath = null)
    {
        _defaultsPath = defaultsPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DownloaderV2", "settings.json");
        _developmentPath = developmentPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
    }

    public string UserSettingsPath => _settingsPath;
    public string GetDownloadFolder() => GetPath("DownloadFolder", DefaultDownloadFolder());
    public string GetMusicLibraryFolder() => GetPath("MusicLibraryFolder", DefaultMusicLibraryFolder());
    public string GetMusicDownloadFolder() => GetPath("MusicDownloadFolder", Path.Combine(GetMusicLibraryFolder(), "Tracks"));
    public string GetRadioCacheFolder() => GetPath("RadioCacheFolder", DefaultRadioCacheFolder());
    public string GetPreferredVisualizer() => GetValue("PreferredVisualizer") ?? Visualizers.VisualizerCatalog.DefaultName;
    public string? GetUserLastFmApiKey() => GetLastFmApiKey(ReadSettings(_settingsPath));
    public int GetRadioPreloadCount() => Math.Clamp(GetRadioInt("PreloadCount", 2), 1, 5);
    public int GetRadioCacheMaxAgeDays() => Math.Clamp(GetRadioInt("CacheMaxAgeDays", 7), 1, 90);
    public double GetRadioCacheMaxSizeGb() => Math.Clamp(GetRadioDouble("CacheMaxSizeGb", 5), 0.25, 50);

    public string? GetLastFmApiKey()
    {
        var userKey = GetUserLastFmApiKey();
        if (!string.IsNullOrWhiteSpace(userKey)) return userKey;
#if DEBUG
        return GetLastFmApiKey(ReadSettings(_developmentPath));
#else
        return null;
#endif
    }

    public Task SaveDownloadFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        SaveValueAsync("DownloadFolder", folder, cancellationToken);

    public Task SaveMusicLibraryFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        SaveValueAsync("MusicLibraryFolder", folder, cancellationToken);

    public Task SaveMusicDownloadFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        SaveValueAsync("MusicDownloadFolder", folder, cancellationToken);

    public Task SaveRadioCacheFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        SaveValueAsync("RadioCacheFolder", folder, cancellationToken);

    public Task SavePreferredVisualizerAsync(string name, CancellationToken cancellationToken = default) =>
        SaveValueAsync("PreferredVisualizer", name, cancellationToken);

    public async Task SaveLastFmApiKeyAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var settings = ReadSettings(_settingsPath) ?? new JsonObject();
            var lastFm = settings["LastFm"] as JsonObject ?? new JsonObject();
            lastFm["ApiKey"] = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            settings["LastFm"] = lastFm;
            await WriteSettingsAsync(settings, cancellationToken);
        }
        finally { _writeLock.Release(); }
    }

    public async Task SaveRadioSettingsAsync(int preloadCount, int maxAgeDays, double maxSizeGb, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var settings = ReadSettings(_settingsPath) ?? new JsonObject();
            settings["Radio"] = new JsonObject
            {
                ["PreloadCount"] = Math.Clamp(preloadCount, 1, 5),
                ["CacheMaxAgeDays"] = Math.Clamp(maxAgeDays, 1, 90),
                ["CacheMaxSizeGb"] = Math.Clamp(maxSizeGb, 0.25, 50)
            };
            await WriteSettingsAsync(settings, cancellationToken);
        }
        finally { _writeLock.Release(); }
    }

    private string GetPath(string key, string fallback)
    {
        var value = GetValue(key);
        if (string.IsNullOrWhiteSpace(value)) value = fallback;
        try
        {
            value = Environment.ExpandEnvironmentVariables(value);
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return fallback;
        }
    }

    private string? GetValue(string key)
    {
        var user = ReadSettings(_settingsPath);
        if (user?[key] is JsonValue userValue && userValue.TryGetValue<string>(out var userText) && !string.IsNullOrWhiteSpace(userText))
            return userText;

        var defaults = ReadSettings(_defaultsPath);
        return defaults?[key] is JsonValue defaultValue && defaultValue.TryGetValue<string>(out var defaultText)
            ? defaultText
            : null;
    }

    private static JsonObject? ReadSettings(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? GetLastFmApiKey(JsonObject? settings) =>
        settings?["LastFm"] is JsonObject lastFm &&
        lastFm["ApiKey"] is JsonValue apiKey &&
        apiKey.TryGetValue<string>(out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private int GetRadioInt(string name, int fallback)
    {
        var radio = ReadSettings(_settingsPath)?["Radio"] as JsonObject;
        return radio?[name] is JsonValue value && value.TryGetValue<int>(out var result) ? result : fallback;
    }

    private double GetRadioDouble(string name, double fallback)
    {
        var radio = ReadSettings(_settingsPath)?["Radio"] as JsonObject;
        return radio?[name] is JsonValue value && value.TryGetValue<double>(out var result) ? result : fallback;
    }

    private async Task SaveValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var settings = ReadSettings(_settingsPath) ?? new JsonObject();
            settings[key] = value;
            await WriteSettingsAsync(settings, cancellationToken);
        }
        finally { _writeLock.Release(); }
    }

    private async Task WriteSettingsAsync(JsonObject settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string DefaultDownloadFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "VideoDownloader");

    private static string DefaultMusicLibraryFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "DownloaderV2");

    private static string DefaultRadioCacheFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloaderV2", "RadioCache");
}
