namespace DownloaderV2.Models;

public enum AnalysisStatus
{
    Success,
    OnlyDrmFormats,
    NoUsableFormats,
    UnsupportedUrl,
    UnavailableOrPrivate,
    NetworkOrExtractionFailure
}
