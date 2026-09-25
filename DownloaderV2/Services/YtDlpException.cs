namespace DownloaderV2.Services;

public sealed class YtDlpException(string message, string details = "", Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Details { get; } = details;
}
