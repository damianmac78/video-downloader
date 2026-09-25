namespace DownloaderV2.Services;

public static class StorageService
{
    public static void EnsureWritableDirectory(string path, string description)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".downloader-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new IOException($"The {description} is unavailable or not writable: {path}", ex);
        }
    }
}
