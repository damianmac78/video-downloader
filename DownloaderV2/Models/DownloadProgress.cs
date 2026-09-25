namespace DownloaderV2.Models;

public sealed record DownloadProgress(double Percentage, string Speed, string Eta, string StatusText);
