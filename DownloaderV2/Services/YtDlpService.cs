using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DownloaderV2.Models;

namespace DownloaderV2.Services;

public sealed partial class YtDlpService
{
    private static readonly ClientStrategy[] YoutubeStrategies =
    [
        new(YoutubeClientStrategy.Default, null),
        new(YoutubeClientStrategy.DefaultWithoutVisionOs, "default,-visionos"),
        new(YoutubeClientStrategy.WebSafari, "web_safari"),

        // Current yt-dlp reports that mweb HTTPS formats require a GVS PO token.
        // This application deliberately has no cookie or PO-token configuration,
        // so mweb remains represented but is skipped rather than partially used.
        new(YoutubeClientStrategy.MWeb, "mweb", IsEnabled: false,
            SkipReason: "mweb was skipped because the current yt-dlp client requires GVS PO-token configuration.")
    ];

    private readonly string _toolsFolder;
    private readonly string? _javaScriptRuntime;
    private string YtDlpPath => Path.Combine(_toolsFolder, "yt-dlp.exe");

    public YtDlpService() : this(Path.Combine(AppContext.BaseDirectory, "Tools"))
    {
    }

    public YtDlpService(string toolsFolder)
    {
        _toolsFolder = toolsFolder;
        _javaScriptRuntime = FindJavaScriptRuntime();
    }

    public async Task<AnalysisResult> AnalyzeAsync(string url, CancellationToken cancellationToken = default)
    {
        EnsureToolsAvailable(requireFfmpeg: false);
        var isYoutube = IsYoutubeUrl(url);
        var strategies = isYoutube
            ? YoutubeStrategies
            : [new ClientStrategy(YoutubeClientStrategy.Default, null)];

        var diagnostics = new StringBuilder();
        var attemptedStatuses = new List<AnalysisStatus>();

        foreach (var strategy in strategies)
        {
            if (!strategy.IsEnabled)
            {
                AppendDiagnostic(diagnostics, strategy.Name, strategy.SkipReason ?? "Strategy skipped.");
                continue;
            }

            var attempt = await AnalyzeWithStrategyAsync(url, strategy, isYoutube, cancellationToken);
            attemptedStatuses.Add(attempt.Status);
            AppendDiagnostic(diagnostics, strategy.Name, attempt.StandardError);

            if (attempt.Status == AnalysisStatus.Success && attempt.VideoInfo is not null)
            {
                YoutubeClientStrategy? successfulStrategy = isYoutube ? strategy.Name : null;
                Debug.WriteLine($"yt-dlp analysis succeeded with strategy: {successfulStrategy?.ToString() ?? "Normal"}");
                return new AnalysisResult(
                    AnalysisStatus.Success,
                    attempt.VideoInfo,
                    successfulStrategy,
                    successfulStrategy is YoutubeClientStrategy.Default or null
                        ? "Analysis completed."
                        : $"Analysis completed using YouTube client strategy {successfulStrategy}.",
                    diagnostics.ToString().Trim());
            }

            // Client fallback is appropriate only when extraction worked but exposed
            // DRM-only content or no actual audio/video formats. Other errors are not
            // improved by sending more player-client requests.
            if (!isYoutube || attempt.Status is not (AnalysisStatus.OnlyDrmFormats or AnalysisStatus.NoUsableFormats))
            {
                return new AnalysisResult(
                    attempt.Status,
                    null,
                    null,
                    FriendlyFailure(attempt.Status),
                    diagnostics.ToString().Trim());
            }
        }

        var finalStatus = attemptedStatuses.Count > 0 && attemptedStatuses.All(status => status == AnalysisStatus.OnlyDrmFormats)
            ? AnalysisStatus.OnlyDrmFormats
            : AnalysisStatus.NoUsableFormats;

        return new AnalysisResult(
            finalStatus,
            null,
            null,
            "No non-DRM downloadable formats were found for this video.",
            diagnostics.ToString().Trim());
    }

    public async Task<DownloadResult> DownloadAsync(
        string url,
        VideoFormatOption format,
        string downloadFolder,
        YoutubeClientStrategy? youtubeStrategy,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureToolsAvailable(requireFfmpeg: true);
        Directory.CreateDirectory(downloadFolder);

        var strategy = IsYoutubeUrl(url)
            ? YoutubeStrategies.FirstOrDefault(item => item.Name == (youtubeStrategy ?? YoutubeClientStrategy.Default))
            : null;

        var startInfo = CreateStartInfo(strategy);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(format.FormatSelector);
        startInfo.ArgumentList.Add("--ffmpeg-location");
        startInfo.ArgumentList.Add(_toolsFolder);
        if (!format.AudioOnly)
        {
            startInfo.ArgumentList.Add("--merge-output-format");
            startInfo.ArgumentList.Add("mp4");
        }
        else
        {
            startInfo.ArgumentList.Add("--add-metadata");
            startInfo.ArgumentList.Add("--embed-thumbnail");
            startInfo.ArgumentList.Add("--convert-thumbnails");
            startInfo.ArgumentList.Add("jpg");
        }
        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--print");
        startInfo.ArgumentList.Add("after_move:__DOWNLOADER_FILE__%(filepath)s");
        // --print enables quiet mode in yt-dlp; explicitly retain the progress stream
        // consumed by the WPF progress bar.
        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add(downloadFolder);
        startInfo.ArgumentList.Add(url);

        using var process = new Process { StartInfo = startInfo };
        var errors = new StringBuilder();
        string? outputFilePath = null;
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            const string outputMarker = "__DOWNLOADER_FILE__";
            if (e.Data.StartsWith(outputMarker, StringComparison.Ordinal))
            {
                outputFilePath = e.Data[outputMarker.Length..].Trim();
                return;
            }

            progress?.Report(ParseProgress(e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) errors.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new YtDlpException($"The download failed (yt-dlp exit code {process.ExitCode}).", errors.ToString().Trim());
            }

            return new DownloadResult(outputFilePath);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (YtDlpException) { throw; }
        catch (Exception ex)
        {
            throw new YtDlpException("The download could not be started.", ex.Message, ex);
        }
    }

    private async Task<AnalysisAttempt> AnalyzeWithStrategyAsync(
        string url,
        ClientStrategy strategy,
        bool inspectFormats,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(strategy);
        startInfo.ArgumentList.Add("--dump-single-json");
        startInfo.ArgumentList.Add(url);

        ProcessResult processResult;
        try
        {
            processResult = await RunCaptureAsync(startInfo, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new YtDlpException("yt-dlp could not be started.", ex.Message, ex);
        }

        if (processResult.ExitCode != 0)
        {
            return new AnalysisAttempt(ClassifyFailure(processResult.StandardError), null, processResult.StandardError);
        }

        try
        {
            using var document = JsonDocument.Parse(processResult.StandardOutput);
            var root = document.RootElement;
            var videoInfo = CreateVideoInfo(root);

            if (!inspectFormats)
            {
                return new AnalysisAttempt(AnalysisStatus.Success, videoInfo, processResult.StandardError);
            }

            var formatInspection = InspectFormats(root, processResult.StandardError);
            return new AnalysisAttempt(formatInspection, formatInspection == AnalysisStatus.Success ? videoInfo : null, processResult.StandardError);
        }
        catch (JsonException ex)
        {
            var details = string.Join(Environment.NewLine, processResult.StandardError, ex.Message).Trim();
            return new AnalysisAttempt(AnalysisStatus.NetworkOrExtractionFailure, null, details);
        }
    }

    private static AnalysisStatus InspectFormats(JsonElement root, string standardError)
    {
        var foundDrmMedia = false;

        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var format in formats.EnumerateArray())
            {
                if (!IsAudioOrVideoFormat(format)) continue;

                if (HasDrm(format))
                {
                    foundDrmMedia = true;
                    continue;
                }

                return AnalysisStatus.Success;
            }
        }

        if (foundDrmMedia || ContainsDrmMessage(standardError)) return AnalysisStatus.OnlyDrmFormats;
        return AnalysisStatus.NoUsableFormats;
    }

    private static bool IsAudioOrVideoFormat(JsonElement format)
    {
        var formatId = GetNullableString(format, "format_id");
        var extension = GetNullableString(format, "ext");
        var protocol = GetNullableString(format, "protocol");
        var videoCodec = GetNullableString(format, "vcodec");
        var audioCodec = GetNullableString(format, "acodec");

        if (extension?.Equals("mhtml", StringComparison.OrdinalIgnoreCase) == true ||
            protocol?.Equals("mhtml", StringComparison.OrdinalIgnoreCase) == true ||
            formatId?.StartsWith("sb", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        return IsCodecPresent(videoCodec) || IsCodecPresent(audioCodec);
    }

    private static bool IsCodecPresent(string? codec) =>
        !string.IsNullOrWhiteSpace(codec) && !codec.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static bool HasDrm(JsonElement format)
    {
        if (!format.TryGetProperty("has_drm", out var drm)) return false;
        return drm.ValueKind == JsonValueKind.True ||
               (drm.ValueKind == JsonValueKind.String && bool.TryParse(drm.GetString(), out var value) && value);
    }

    private static AnalysisStatus ClassifyFailure(string standardError)
    {
        var error = standardError.ToLowerInvariant();

        if (ContainsAny(error, "unsupported url", "no suitable extractor", "not a valid url", "invalid url"))
            return AnalysisStatus.UnsupportedUrl;

        if (ContainsAny(error, "private video", "video unavailable", "this video is unavailable", "members-only", "members only", "premium video", "login required"))
            return AnalysisStatus.UnavailableOrPrivate;

        if (ContainsDrmMessage(error))
            return AnalysisStatus.OnlyDrmFormats;

        if (ContainsAny(error, "no video formats found", "requested format is not available", "only images are available"))
            return AnalysisStatus.NoUsableFormats;

        return AnalysisStatus.NetworkOrExtractionFailure;
    }

    private static bool ContainsDrmMessage(string value) =>
        value.Contains("drm protected", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("drm-protected", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);

    private static string FriendlyFailure(AnalysisStatus status) => status switch
    {
        AnalysisStatus.UnsupportedUrl => "This URL is not supported.",
        AnalysisStatus.UnavailableOrPrivate => "This video is unavailable or private.",
        AnalysisStatus.OnlyDrmFormats or AnalysisStatus.NoUsableFormats => "No non-DRM downloadable formats were found for this video.",
        _ => "The video could not be analysed because of a network or extraction error."
    };

    private ProcessStartInfo CreateStartInfo(ClientStrategy? strategy = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_javaScriptRuntime is not null)
        {
            startInfo.ArgumentList.Add("--js-runtimes");
            startInfo.ArgumentList.Add(_javaScriptRuntime);
        }

        if (!string.IsNullOrWhiteSpace(strategy?.ExtractorArgument))
        {
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add($"youtube:player_client={strategy.ExtractorArgument}");
        }

        return startInfo;
    }

    private static async Task<ProcessResult> RunCaptureAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private string? FindJavaScriptRuntime()
    {
        var bundledDeno = Path.Combine(_toolsFolder, "deno.exe");
        if (File.Exists(bundledDeno)) return $"deno:{bundledDeno}";

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installedNode = Path.Combine(programFiles, "nodejs", "node.exe");
        if (File.Exists(installedNode)) return $"node:{installedNode}";

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in pathEntries)
        {
            var deno = Path.Combine(entry, "deno.exe");
            if (File.Exists(deno)) return $"deno:{deno}";

            var node = Path.Combine(entry, "node.exe");
            if (File.Exists(node)) return $"node:{node}";
        }

        return null;
    }

    private void EnsureToolsAvailable(bool requireFfmpeg)
    {
        if (!File.Exists(YtDlpPath))
            throw new YtDlpException("yt-dlp.exe could not be found.", $"Expected location: {YtDlpPath}");

        var ffmpegPath = Path.Combine(_toolsFolder, "ffmpeg.exe");
        if (requireFfmpeg && !File.Exists(ffmpegPath))
            throw new YtDlpException("ffmpeg.exe could not be found.", $"Expected location: {ffmpegPath}");
    }

    private static bool IsYoutubeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.EndsWith(".youtu.be", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendDiagnostic(StringBuilder builder, YoutubeClientStrategy strategy, string message)
    {
        if (builder.Length > 0) builder.AppendLine().AppendLine();
        builder.Append('[').Append(strategy).AppendLine("]");
        builder.Append(string.IsNullOrWhiteSpace(message) ? "No stderr output." : message.Trim());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    internal static DownloadProgress ParseProgress(string line)
    {
        var percentMatch = PercentageRegex().Match(line);
        if (percentMatch.Success)
        {
            _ = double.TryParse(percentMatch.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage);
            var speedMatch = SpeedRegex().Match(line);
            var etaMatch = EtaRegex().Match(line);
            var speed = speedMatch.Success ? speedMatch.Groups["speed"].Value : "—";
            var eta = etaMatch.Success ? etaMatch.Groups["eta"].Value : "—";
            return new DownloadProgress(percentage, speed, eta, line.Trim());
        }
        return new DownloadProgress(0, "—", "—", FriendlyStatus(line));
    }

    private static string FriendlyStatus(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase)) return "Merging video and audio…";
        if (trimmed.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase)) return "Preparing audio…";
        if (trimmed.StartsWith("[download] Destination", StringComparison.OrdinalIgnoreCase)) return "Starting download…";
        if (trimmed.StartsWith("[download]", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("already been downloaded", StringComparison.OrdinalIgnoreCase)) return "File already downloaded.";
        return trimmed;
    }

    private static VideoInfo CreateVideoInfo(JsonElement root) => new(
        GetString(root, "title", "Untitled video"),
        GetString(root, "uploader", "Unknown uploader"),
        TryGetDuration(root),
        GetNullableString(root, "thumbnail"),
        GetNullableString(root, "id"),
        GetNullableString(root, "webpage_url") ?? GetNullableString(root, "original_url"));

    private static string GetString(JsonElement root, string name, string fallback) => GetNullableString(root, name) ?? fallback;
    private static string? GetNullableString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static TimeSpan? TryGetDuration(JsonElement root) =>
        root.TryGetProperty("duration", out var value) && value.TryGetDouble(out var seconds) ? TimeSpan.FromSeconds(seconds) : null;

    private sealed record ClientStrategy(
        YoutubeClientStrategy Name,
        string? ExtractorArgument,
        bool IsEnabled = true,
        string? SkipReason = null);

    private sealed record AnalysisAttempt(AnalysisStatus Status, VideoInfo? VideoInfo, string StandardError);
    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    [GeneratedRegex(@"^\[download\]\s+(?<percent>\d+(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex PercentageRegex();

    [GeneratedRegex(@"\sat\s+(?<speed>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex SpeedRegex();

    [GeneratedRegex(@"\sETA\s+(?<eta>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex EtaRegex();
}
