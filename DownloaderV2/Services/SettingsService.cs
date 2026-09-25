using System.Text.Json;
using System.Text.Json.Nodes;

namespace DownloaderV2.Services;

public sealed class SettingsService
{
    private readonly string _defaultsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DownloaderV2", "settings.json");
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string UserSettingsPath => _settingsPath;
    public string GetDownloadFolder() => GetPath("DownloadFolder", DefaultDownloadFolder());
    public string GetMusicLibraryFolder() => GetPath("MusicLibraryFolder", DefaultMusicLibraryFolder());
    public string GetPreferredVisualizer() => GetValue("PreferredVisualizer") ?? Visualizers.VisualizerCatalog.DefaultName;

    public Task SaveDownloadFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        SaveValueAsync("DownloadFolder", folder, cancellationToken);

    public Task SaveMusicLibraryFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        SaveValueAsync("MusicLibraryFolder", folder, cancellationToken);

    public Task SavePreferredVisualizerAsync(string name, CancellationToken cancellationToken = default) =>
        SaveValueAsync("PreferredVisualizer", name, cancellationToken);

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

    private async Task SaveValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var settings = ReadSettings(_settingsPath) ?? new JsonObject();
            settings[key] = value;
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
        finally { _writeLock.Release(); }
    }

    private static string DefaultDownloadFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "VideoDownloader");

    private static string DefaultMusicLibraryFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "DownloaderV2");
}
