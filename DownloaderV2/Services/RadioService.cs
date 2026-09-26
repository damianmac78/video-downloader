using DownloaderV2.Models;

namespace DownloaderV2.Services;

public enum RadioState { Off, Preparing, Active, WaitingForRecommendation, Downloading, FallbackToLibrary, Error }

public interface IRadioService : IDisposable
{
    event EventHandler? StateChanged;
    event EventHandler<Track>? TrackKept;
    bool IsEnabled { get; }
    RadioState State { get; }
    string StatusText { get; }
    void SetEnabled(bool enabled, Track? seed);
    void TrackStarted(Track track);
    Task<Track> KeepAsync(QueueItem item, CancellationToken cancellationToken = default);
}

public sealed class RadioService(
    IMusicDiscoveryService discovery,
    AudioLibraryDownloadService downloader,
    MusicLibraryService library,
    IPlaybackQueueService queue,
    RadioCacheService cache,
    SettingsService settings) : IRadioService
{
    private readonly object _sync = new();
    private readonly Queue<string> _recent = new();
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private CancellationTokenSource? _workCancellation;
    private int _generation;
    private bool _disposed;
    private Track? _currentTrack;

    public event EventHandler? StateChanged;
    public event EventHandler<Track>? TrackKept;
    public bool IsEnabled { get; private set; }
    public RadioState State { get; private set; } = RadioState.Off;
    public string StatusText { get; private set; } = "Radio off";

    public void SetEnabled(bool enabled, Track? seed)
    {
        if (_disposed) return;
        IsEnabled = enabled;
        if (!enabled)
        {
            CancelWork();
            queue.Clear(radioOnly: true);
            SetState(RadioState.Off, "Radio off");
            return;
        }
        if (seed is null)
        {
            IsEnabled = false;
            SetState(RadioState.Error, "Play a local track before enabling Radio.");
            return;
        }
        TrackStarted(seed);
    }

    public void TrackStarted(Track track)
    {
        if (_disposed) return;
        _currentTrack = track;
        AddRecent(track);
        if (!IsEnabled) return;
        CancelWork();
        var cancellation = new CancellationTokenSource();
        _workCancellation = cancellation;
        var generation = Interlocked.Increment(ref _generation);
        _ = FillQueueAsync(track, generation, cancellation.Token).ContinueWith(_task =>
        {
            Interlocked.CompareExchange(ref _workCancellation, null, cancellation);
            cancellation.Dispose();
        }, TaskScheduler.Default);
    }

    public async Task<Track> KeepAsync(QueueItem item, CancellationToken cancellationToken = default)
    {
        if (!item.IsTemporary || item.LocalTrack is null) return item.LocalTrack ?? throw new InvalidOperationException("This queue item has no track.");
        var kept = await cache.KeepAsync(item.LocalTrack, cancellationToken);
        item.LocalTrack = kept;
        item.IsTemporary = false;
        TrackKept?.Invoke(this, kept);
        return kept;
    }

    private async Task FillQueueAsync(Track seed, int generation, CancellationToken cancellationToken)
    {
        try
        {
            cache.EnsureFolders();
            SetState(RadioState.Preparing, "Preparing Radio…");
            var attempts = 0;
            while (IsCurrent(generation) && PreparedCount() < settings.GetRadioPreloadCount() && attempts++ < 4)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var placeholder = new QueueItem { IsRadioGenerated = true, Status = QueueItemStatus.PendingDiscovery };
                queue.Enqueue(placeholder);
                SetState(RadioState.WaitingForRecommendation, "Finding a recommendation…");
                IReadOnlyList<DiscoveryTrack> recommendations;
                try { recommendations = await discovery.FindSimilarAsync(seed, cancellationToken); }
                catch (OperationCanceledException) { queue.Remove(placeholder.Id); throw; }
                catch { queue.Remove(placeholder.Id); recommendations = []; }

                var candidates = recommendations.Where(IsCandidate).ToArray();
                var candidate = candidates.FirstOrDefault(item => !MusicMetadata.Normalize(item.Artist).Equals(MusicMetadata.Normalize(seed.Artist), StringComparison.Ordinal))
                                ?? candidates.FirstOrDefault();
                queue.Remove(placeholder.Id);
                if (candidate is null)
                {
                    if (!await EnqueueFallbackAsync(cancellationToken)) break;
                    continue;
                }

                var identity = MusicMetadata.Identity(candidate.Artist, candidate.Title);
                if (candidate.ExistingTrack is { } existing)
                {
                    queue.Enqueue(new QueueItem { LocalTrack = existing, PendingDiscovery = candidate, IsRadioGenerated = true, Status = QueueItemStatus.Ready });
                    SetState(RadioState.Active, "Radio ready");
                    continue;
                }

                var item = new QueueItem { PendingDiscovery = candidate, IsRadioGenerated = true, IsTemporary = true, Status = QueueItemStatus.Downloading };
                queue.Enqueue(item);
                SetState(RadioState.Downloading, $"Preparing {candidate.Artist} — {candidate.Title}");
                try
                {
                    var info = new VideoInfo(candidate.Title, candidate.Artist, candidate.Duration, candidate.ThumbnailUrl, candidate.VideoId, candidate.SourceUrl);
                    var progress = new Progress<DownloadProgress>(value => item.Progress = value.Percentage);
                    var downloaded = await downloader.DownloadTemporaryAsync(candidate.SourceUrl, info, cache.TracksPath, cache.ArtworkPath, progress, cancellationToken);
                    if (!IsCurrent(generation))
                    {
                        DeleteTemporary(downloaded);
                        queue.Remove(item.Id);
                        break;
                    }
                    await cache.RegisterAsync(downloaded, cancellationToken);
                    item.LocalTrack = downloaded;
                    item.Status = QueueItemStatus.Ready;
                    SetState(RadioState.Active, "Radio ready");
                }
                catch (OperationCanceledException) { queue.Remove(item.Id); throw; }
                catch
                {
                    AddFailed(identity);
                    item.Status = QueueItemStatus.Failed;
                    queue.Remove(item.Id);
                }
            }
            if (IsCurrent(generation))
            {
                var protectedPaths = queue.Snapshot
                    .SelectMany(item => new[] { item.LocalTrack?.FilePath, item.LocalTrack?.ArtworkPath })
                    .OfType<string>()
                    .Concat(_currentTrack is null ? [] : new[] { _currentTrack.FilePath, _currentTrack.ArtworkPath }.OfType<string>());
                cache.Cleanup(protectedPaths);
                if (PreparedCount() > 0) SetState(RadioState.Active, "Radio ready");
                else
                {
                    IsEnabled = false;
                    SetState(RadioState.Error, "Radio could not find a playable next track.");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { if (IsCurrent(generation)) SetState(RadioState.Error, "Radio encountered an error while preparing the queue."); }
    }

    private bool IsCandidate(DiscoveryTrack candidate)
    {
        var identity = MusicMetadata.Identity(candidate.Artist, candidate.Title);
        if (WasFailed(identity) || WasRecent(identity)) return false;
        if (candidate.ExistingTrack is { } local) return File.Exists(local.FilePath) && !queue.ContainsTrack(local);
        return !string.IsNullOrWhiteSpace(candidate.SourceUrl) && !queue.Snapshot.Any(item => item.PendingDiscovery is { } pending &&
            MusicMetadata.Identity(pending.Artist, pending.Title) == identity);
    }

    private async Task<bool> EnqueueFallbackAsync(CancellationToken cancellationToken)
    {
        SetState(RadioState.FallbackToLibrary, "Using a track from your library…");
        var choices = (await library.GetTracksAsync(cancellationToken))
            .Where(track => File.Exists(track.FilePath) && !WasRecent(MusicMetadata.Identity(track.Artist, track.Title)) && !queue.ContainsTrack(track))
            .ToArray();
        if (choices.Length == 0) return false;
        var fallback = choices[Random.Shared.Next(choices.Length)];
        queue.Enqueue(new QueueItem { LocalTrack = fallback, IsRadioGenerated = true, Status = QueueItemStatus.Ready });
        return true;
    }

    private int PreparedCount() => queue.Snapshot.Count(item => item.IsRadioGenerated && item.Status is QueueItemStatus.PendingDiscovery or QueueItemStatus.Downloading or QueueItemStatus.Ready);
    private bool IsCurrent(int generation) => IsEnabled && generation == Volatile.Read(ref _generation);

    private void AddRecent(Track track)
    {
        var identity = MusicMetadata.Identity(track.Artist, track.Title);
        lock (_sync)
        {
            _recent.Enqueue(identity);
            while (_recent.Count > 40) _recent.Dequeue();
        }
    }

    private bool WasRecent(string identity) { lock (_sync) return _recent.Contains(identity); }
    private bool WasFailed(string identity) { lock (_sync) return _failed.Contains(identity); }
    private void AddFailed(string identity) { lock (_sync) _failed.Add(identity); }

    private void SetState(RadioState state, string text)
    {
        State = state;
        StatusText = text;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CancelWork()
    {
        Interlocked.Increment(ref _generation);
        var cancellation = Interlocked.Exchange(ref _workCancellation, null);
        cancellation?.Cancel();
    }

    private static void DeleteTemporary(Track track)
    {
        try { if (File.Exists(track.FilePath)) File.Delete(track.FilePath); } catch { }
        try { if (!string.IsNullOrWhiteSpace(track.ArtworkPath) && File.Exists(track.ArtworkPath)) File.Delete(track.ArtworkPath); } catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        IsEnabled = false;
        CancelWork();
        GC.SuppressFinalize(this);
    }
}
