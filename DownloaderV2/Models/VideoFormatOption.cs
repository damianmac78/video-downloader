namespace DownloaderV2.Models;

public sealed record VideoFormatOption(string Name, string FormatSelector, bool AudioOnly = false)
{
    public override string ToString() => Name;

    public static IReadOnlyList<VideoFormatOption> Defaults { get; } =
    [
        new("Best", "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]"),
        new("1080p", "bv*[ext=mp4][height<=1080]+ba[ext=m4a]/b[ext=mp4][height<=1080]"),
        new("720p", "bv*[ext=mp4][height<=720]+ba[ext=m4a]/b[ext=mp4][height<=720]"),
        new("480p", "bv*[ext=mp4][height<=480]+ba[ext=m4a]/b[ext=mp4][height<=480]"),
        new("Audio only", "ba[ext=m4a]/ba", true)
    ];
}
