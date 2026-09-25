# DownloaderV2

DownloaderV2 is a compact Windows desktop downloader and music-library player built with .NET 10 WPF. It uses bundled `yt-dlp`, `ffmpeg`, and `ffprobe` executables for media inspection and processing, SQLite for the local library, and NAudio for playback and audio-reactive visualisers.

## Features

- Analyse and download supported media URLs with selectable quality
- YouTube client fallback for finding ordinary non-DRM formats
- Audio-only downloads added to a searchable local music library
- Playlists and persistent player controls
- 25 audio-reactive visualisers with a resizable/full-screen pop-out window
- About / Diagnostics view with tool versions, storage paths, and a manual yt-dlp update check

DownloaderV2 does not decrypt or bypass DRM. Protected, private, unavailable, or account-restricted media may not be downloadable.

## Requirements for development

- Windows 10 or newer, x64
- .NET 10 SDK
- Visual Studio with the .NET desktop development workload, or the `dotnet` CLI
- These Windows executables in `DownloaderV2/Tools/`:
  - `yt-dlp.exe`
  - `ffmpeg.exe`
  - `ffprobe.exe`

The tool binaries are intentionally ignored by Git. Obtain them from their official projects or a trusted FFmpeg Windows distributor, verify their provenance, and review their licenses before distribution.

## Build and run

From the repository root:

```powershell
dotnet restore .\DownloaderV2.slnx
dotnet build .\DownloaderV2.slnx -c Release
dotnet run --project .\DownloaderV2\DownloaderV2.csproj -c Release
```

The tools and `appsettings.json` are copied into the output directory automatically when present.

## Self-contained Windows release

Use the included non-single-file `win-x64` publish profile:

```powershell
dotnet publish .\DownloaderV2\DownloaderV2.csproj -p:PublishProfile=win-x64
```

The release is written to:

```text
DownloaderV2\bin\Release\net10.0-windows\win-x64\publish\
```

Distribute the entire publish directory. It includes the .NET runtime and does not require the recipient to install .NET separately. Before shipping, verify that `Tools/yt-dlp.exe`, `Tools/ffmpeg.exe`, `Tools/ffprobe.exe`, `appsettings.json`, and `THIRD-PARTY-NOTICES.txt` are present.

## Storage

Defaults can be changed in `appsettings.json` using environment variables:

- Video downloads: `%USERPROFILE%\Downloads\VideoDownloader`
- Music library: `%USERPROFILE%\Music\DownloaderV2`
- Tracks: the `Tracks` directory inside the music library
- Artwork cache: the `Artwork` directory inside the music library
- SQLite database: `library.db` inside the music library
- User preferences: `%LOCALAPPDATA%\DownloaderV2\settings.json`

If a configured storage location is unavailable or not writable, the application reports it and uses a writable directory below `%LOCALAPPDATA%\DownloaderV2`.

## Updating yt-dlp

Open **About / Diagnostics** and select **Check for yt-dlp update**. Updates are never run automatically at startup. The updater requires write access to the published `Tools` directory; if the app is installed under a protected directory, update the bundled executable as an administrator or replace it during application deployment.

## Known limitations

- DRM-protected media is not decrypted or downloaded.
- Some YouTube formats require a supported JavaScript runtime or PO-token configuration. DownloaderV2 does not scrape cookies or work around access controls.
- Site changes can temporarily require a newer yt-dlp version.
- A portable build cannot guarantee every codec/container combination supported by third-party sites.
- Public redistribution requires review of the bundled tools' licenses and complete accompanying notices; see `THIRD-PARTY-NOTICES.txt`.
