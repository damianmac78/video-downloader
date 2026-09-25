using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace DownloaderV2.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public string GetDownloadFolder()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var configuredFolder = configuration["DownloadFolder"];
        if (string.IsNullOrWhiteSpace(configuredFolder))
        {
            configuredFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "VideoDownloader");
        }
        return Environment.ExpandEnvironmentVariables(configuredFolder);
    }

    public string GetMusicLibraryFolder()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var configuredFolder = configuration["MusicLibraryFolder"];
        if (string.IsNullOrWhiteSpace(configuredFolder))
        {
            configuredFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "DownloaderV2");
        }

        return Environment.ExpandEnvironmentVariables(configuredFolder);
    }

    public async Task SaveDownloadFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        JsonObject settings;
        try
        {
            settings = JsonNode.Parse(await File.ReadAllTextAsync(_settingsPath, cancellationToken))?.AsObject() ?? new JsonObject();
        }
        catch (FileNotFoundException)
        {
            settings = new JsonObject();
        }
        catch (JsonException)
        {
            settings = new JsonObject();
        }

        settings["DownloadFolder"] = folder;
        await File.WriteAllTextAsync(_settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
