namespace DownloaderV2.Models;

public sealed record AnalysisResult(
    AnalysisStatus Status,
    VideoInfo? VideoInfo,
    YoutubeClientStrategy? SuccessfulStrategy,
    string DiagnosticMessage,
    string RawStdErr)
{
    public bool IsSuccess => Status == AnalysisStatus.Success && VideoInfo is not null;
}
