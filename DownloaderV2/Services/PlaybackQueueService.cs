using DownloaderV2.Models;

namespace DownloaderV2.Services;

public interface IPlaybackQueueService
{
    event EventHandler? Changed;
    IReadOnlyList<QueueItem> Snapshot { get; }
    int Count { get; }
    void Enqueue(QueueItem item);
    QueueItem? TakeNextReady();
    void Remove(Guid id);
    void Move(Guid id, int offset);
    void Clear(bool radioOnly = false);
    bool ContainsTrack(Track track);
}

public sealed class PlaybackQueueService : IPlaybackQueueService
{
    private readonly object _sync = new();
    private readonly List<QueueItem> _items = [];

    public event EventHandler? Changed;
    public IReadOnlyList<QueueItem> Snapshot { get { lock (_sync) return _items.ToArray(); } }
    public int Count { get { lock (_sync) return _items.Count; } }

    public void Enqueue(QueueItem item)
    {
        lock (_sync) _items.Add(item);
        item.PropertyChanged += ItemOnPropertyChanged;
        RaiseChanged();
    }

    public QueueItem? TakeNextReady()
    {
        QueueItem? item;
        lock (_sync)
        {
            item = _items.FirstOrDefault(candidate => candidate.Status == QueueItemStatus.Ready && candidate.LocalTrack is not null);
            if (item is not null) _items.Remove(item);
        }
        if (item is not null)
        {
            item.PropertyChanged -= ItemOnPropertyChanged;
            item.Status = QueueItemStatus.Playing;
            RaiseChanged();
        }
        return item;
    }

    public void Remove(Guid id)
    {
        QueueItem? removed;
        lock (_sync)
        {
            removed = _items.FirstOrDefault(item => item.Id == id);
            if (removed is not null) _items.Remove(removed);
        }
        if (removed is not null) removed.PropertyChanged -= ItemOnPropertyChanged;
        RaiseChanged();
    }

    public void Move(Guid id, int offset)
    {
        lock (_sync)
        {
            var index = _items.FindIndex(item => item.Id == id);
            if (index < 0 || _items.Count == 0) return;
            var target = Math.Clamp(index + offset, 0, _items.Count - 1);
            if (index == target) return;
            var item = _items[index];
            _items.RemoveAt(index);
            _items.Insert(target, item);
        }
        RaiseChanged();
    }

    public void Clear(bool radioOnly = false)
    {
        QueueItem[] removed;
        lock (_sync)
        {
            removed = _items.Where(item => !radioOnly || item.IsRadioGenerated).ToArray();
            foreach (var item in removed) _items.Remove(item);
        }
        foreach (var item in removed) item.PropertyChanged -= ItemOnPropertyChanged;
        RaiseChanged();
    }

    public bool ContainsTrack(Track track)
    {
        var identity = MusicMetadata.Identity(track.Artist, track.Title);
        lock (_sync) return _items.Any(item => item.LocalTrack is { } queued &&
            ((!string.IsNullOrWhiteSpace(track.SourceVideoId) && track.SourceVideoId.Equals(queued.SourceVideoId, StringComparison.OrdinalIgnoreCase)) ||
             MusicMetadata.Identity(queued.Artist, queued.Title) == identity));
    }

    private void ItemOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseChanged();
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
