namespace DownloaderV2.Models;

public sealed record DiagnosticsSnapshot(
    string ApplicationVersion,
    string RuntimeVersion,
    string YtDlpVersion,
    string FfmpegVersion,
    string FfprobeVersion,
    string DatabasePath,
    string MusicLibraryPath,
    string DownloadPath,
    string ToolsPath);

public sealed record ToolUpdateResult(bool Success, string Message, string Details);
