using System.Diagnostics;
using System.Reflection;
using DownloaderV2.Models;

namespace DownloaderV2.Services;

public sealed class DiagnosticsService(SettingsService settings, string databasePath, string musicLibraryPath)
{
    private readonly string _toolsPath = Path.Combine(AppContext.BaseDirectory, "Tools");

    public string ToolsPath => _toolsPath;

    public IReadOnlyList<string> GetMissingTools()
    {
        var missing = new List<string>();
        foreach (var name in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" })
            if (!File.Exists(Path.Combine(_toolsPath, name))) missing.Add(name);
        return missing;
    }

    public async Task<DiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var ytDlpTask = GetVersionAsync("yt-dlp.exe", ["--version"], cancellationToken);
        var ffmpegTask = GetVersionAsync("ffmpeg.exe", ["-version"], cancellationToken);
        var ffprobeTask = GetVersionAsync("ffprobe.exe", ["-version"], cancellationToken);
        await Task.WhenAll(ytDlpTask, ffmpegTask, ffprobeTask);

        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly?.GetName().Version?.ToString()
                      ?? "Unknown";
        var plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];

        return new DiagnosticsSnapshot(
            version,
            Environment.Version.ToString(),
            await ytDlpTask,
            await ffmpegTask,
            await ffprobeTask,
            databasePath,
            musicLibraryPath,
            settings.GetDownloadFolder(),
            _toolsPath);
    }

    public async Task<ToolUpdateResult> UpdateYtDlpAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_toolsPath, "yt-dlp.exe");
        if (!File.Exists(path))
            return new ToolUpdateResult(false, "yt-dlp.exe is missing.", $"Expected location: {path}");

        try
        {
            var result = await RunToolAsync(path, ["-U"], TimeSpan.FromMinutes(3), cancellationToken);
            var details = string.Join(Environment.NewLine, result.Output, result.Error).Trim();
            return result.ExitCode == 0
                ? new ToolUpdateResult(true, "yt-dlp update check completed.", details)
                : new ToolUpdateResult(false, "yt-dlp could not be updated.", details);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ToolUpdateResult(false, "The yt-dlp update check timed out.", "The updater did not finish within three minutes.");
        }
        catch (Exception ex)
        {
            return new ToolUpdateResult(false, "yt-dlp could not be updated.", ex.Message);
        }
    }

    private async Task<string> GetVersionAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_toolsPath, fileName);
        if (!File.Exists(path)) return "Missing";
        try
        {
            var result = await RunToolAsync(path, arguments, TimeSpan.FromSeconds(10), cancellationToken);
            var firstLine = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                            ?? result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(firstLine) ? firstLine : "Unavailable";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return "Timed out"; }
        catch { return "Unavailable"; }
    }

    private static async Task<ToolProcessResult> RunToolAsync(string path, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return new ToolProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private sealed record ToolProcessResult(int ExitCode, string Output, string Error);
}
