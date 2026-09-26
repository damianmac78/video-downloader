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
    public string GetPreferredVisualizer() => GetValue("PreferredVisualizer") ?? Visualizers.VisualizerCatalog.DefaultName;
    public string? GetUserLastFmApiKey() => GetLastFmApiKey(ReadSettings(_settingsPath));

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
}
