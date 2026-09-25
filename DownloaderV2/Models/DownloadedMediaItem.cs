namespace DownloaderV2.Models;

public sealed record DownloadedMediaItem(
    string Name,
    string MediaType,
    string Extension,
    string FilePath,
    string Folder,
    DateTime Modified,
    long SizeBytes)
{
    public string ModifiedText => Modified.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string SizeText => SizeBytes switch
    {
        >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824d:0.0} GB",
        >= 1_048_576 => $"{SizeBytes / 1_048_576d:0.0} MB",
        >= 1024 => $"{SizeBytes / 1024d:0.0} KB",
        _ => $"{SizeBytes} B"
    };
}
