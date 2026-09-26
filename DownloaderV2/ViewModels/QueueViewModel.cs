using System.Collections.ObjectModel;
using System.Windows;
using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class QueueViewModel : ObservableObject, IDisposable
{
    private readonly IPlaybackQueueService _queue;
    private readonly PlayerViewModel _player;
    private readonly IRadioService _radio;
    private QueueItem? _selectedItem;

    public QueueViewModel(IPlaybackQueueService queue, PlayerViewModel player, IRadioService radio)
    {
        _queue = queue;
        _player = player;
        _radio = radio;
        _queue.Changed += QueueOnChanged;
        PlayNowCommand = new RelayCommand(PlayNow, () => SelectedItem is { LocalTrack: not null, Status: QueueItemStatus.Ready });
        RemoveCommand = new RelayCommand(Remove, () => SelectedItem is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => SelectedItem is not null);
        MoveDownCommand = new RelayCommand(() => Move(1), () => SelectedItem is not null);
        ClearCommand = new RelayCommand(() => _queue.Clear(), () => Items.Count > 0);
        KeepCommand = new AsyncCommand(KeepAsync, () => SelectedItem is { IsTemporary: true, LocalTrack: not null });
        Refresh();
    }

    public ObservableCollection<QueueItem> Items { get; } = [];
    public RelayCommand PlayNowCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand ClearCommand { get; }
    public AsyncCommand KeepCommand { get; }
    public QueueItem? SelectedItem { get => _selectedItem; set { if (SetProperty(ref _selectedItem, value)) RefreshCommands(); } }

    private void PlayNow() { if (SelectedItem is not null) _player.PlayQueueItem(SelectedItem); }
    private void Remove()
    {
        if (SelectedItem is null) return;
        var removing = SelectedItem;
        _queue.Remove(removing.Id);
        if (removing.IsTemporary && removing.LocalTrack is { } track) DeleteTemporary(track);
    }
    private void Move(int offset) { if (SelectedItem is not null) _queue.Move(SelectedItem.Id, offset); }
    private async Task KeepAsync()
    {
        if (SelectedItem is not null) await _radio.KeepAsync(SelectedItem);
        Refresh();
    }

    private void QueueOnChanged(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(Refresh);
    public void Refresh()
    {
        var selectedId = SelectedItem?.Id;
        Items.Clear();
        foreach (var item in _queue.Snapshot) Items.Add(item);
        SelectedItem = Items.FirstOrDefault(item => item.Id == selectedId);
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        PlayNowCommand.RaiseCanExecuteChanged(); RemoveCommand.RaiseCanExecuteChanged();
        MoveUpCommand.RaiseCanExecuteChanged(); MoveDownCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged(); KeepCommand.RaiseCanExecuteChanged();
    }
    private static void DeleteTemporary(Track track)
    {
        try { if (File.Exists(track.FilePath)) File.Delete(track.FilePath); } catch { }
        try { if (!string.IsNullOrWhiteSpace(track.ArtworkPath) && File.Exists(track.ArtworkPath)) File.Delete(track.ArtworkPath); } catch { }
    }
    public void Dispose() => _queue.Changed -= QueueOnChanged;
}
