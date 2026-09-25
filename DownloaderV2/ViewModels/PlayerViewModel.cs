using System.Windows;
using System.Windows.Threading;
using DownloaderV2.Models;
using DownloaderV2.Services;

namespace DownloaderV2.ViewModels;

public sealed class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly AudioPlayerService _player;
    private readonly DispatcherTimer _positionTimer;
    private readonly Random _random = new();
    private IReadOnlyList<Track> _queue = [];
    private Track? _currentTrack;
    private bool _isPlaying;
    private bool _shuffle;
    private bool _repeat;
    private double _positionSeconds;
    private double _durationSeconds;
    private double _volume = 0.8;
    private float[] _spectrumValues = [];
    private bool _updatingPosition;

    public PlayerViewModel(AudioPlayerService player)
    {
        _player = player;
        _player.PlaybackEnded += PlayerOnPlaybackEnded;
        _player.SpectrumAvailable += PlayerOnSpectrumAvailable;
        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, OnPositionTick, Application.Current.Dispatcher);
        _positionTimer.Start();

        PlayPauseCommand = new RelayCommand(TogglePlay, () => CurrentTrack is not null);
        StopCommand = new RelayCommand(Stop, () => CurrentTrack is not null);
        PreviousCommand = new RelayCommand(Previous, () => _queue.Count > 0);
        NextCommand = new RelayCommand(Next, () => _queue.Count > 0);
        ToggleShuffleCommand = new RelayCommand(() => Shuffle = !Shuffle);
        ToggleRepeatCommand = new RelayCommand(() => Repeat = !Repeat);
    }

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand ToggleShuffleCommand { get; }
    public RelayCommand ToggleRepeatCommand { get; }

    public Track? CurrentTrack { get => _currentTrack; private set { if (SetProperty(ref _currentTrack, value)) { OnPropertyChanged(nameof(HasTrack)); RefreshCommands(); } } }
    public bool HasTrack => CurrentTrack is not null;
    public bool IsPlaying { get => _isPlaying; private set { if (SetProperty(ref _isPlaying, value)) OnPropertyChanged(nameof(PlayPauseText)); } }
    public string PlayPauseText => IsPlaying ? "Pause" : "Play";
    public bool Shuffle { get => _shuffle; set { if (SetProperty(ref _shuffle, value)) OnPropertyChanged(nameof(ShuffleText)); } }
    public string ShuffleText => Shuffle ? "Shuffle On" : "Shuffle";
    public bool Repeat { get => _repeat; set { if (SetProperty(ref _repeat, value)) OnPropertyChanged(nameof(RepeatText)); } }
    public string RepeatText => Repeat ? "Repeat On" : "Repeat";
    public float[] SpectrumValues { get => _spectrumValues; private set => SetProperty(ref _spectrumValues, value); }

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            if (!SetProperty(ref _positionSeconds, value)) return;
            OnPropertyChanged(nameof(ElapsedText));
            OnPropertyChanged(nameof(RemainingText));
            if (!_updatingPosition) _player.Seek(TimeSpan.FromSeconds(value));
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (SetProperty(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(TotalText));
                OnPropertyChanged(nameof(RemainingText));
            }
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value)) _player.Volume = (float)value;
        }
    }

    public string ElapsedText => TimeSpan.FromSeconds(PositionSeconds).ToString(@"m\:ss");
    public string TotalText => TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public string RemainingText => $"-{TimeSpan.FromSeconds(Math.Max(0, DurationSeconds - PositionSeconds)).ToString(@"m\:ss")}";

    public void PlayTrack(Track track, IReadOnlyList<Track>? queue = null)
    {
        if (!File.Exists(track.FilePath)) throw new FileNotFoundException("The audio file could not be found.", track.FilePath);
        _queue = queue is { Count: > 0 } ? queue : [track];
        CurrentTrack = track;
        _player.Load(track.FilePath);
        DurationSeconds = _player.Duration.TotalSeconds;
        PositionSeconds = 0;
        _player.Play();
        IsPlaying = true;
        RefreshCommands();
    }

    private void TogglePlay()
    {
        if (CurrentTrack is null) return;
        if (_player.IsPlaying)
        {
            _player.Pause();
            IsPlaying = false;
        }
        else
        {
            _player.Play();
            IsPlaying = true;
        }
    }

    private void Stop()
    {
        _player.Stop();
        IsPlaying = false;
        PositionSeconds = 0;
    }

    private void Previous()
    {
        if (_queue.Count == 0 || CurrentTrack is null) return;
        var index = IndexOfCurrent();
        var previous = index <= 0 ? _queue.Count - 1 : index - 1;
        PlayTrack(_queue[previous], _queue);
    }

    private void Next()
    {
        if (_queue.Count == 0) return;
        var next = Shuffle && _queue.Count > 1
            ? NextRandomIndex()
            : (IndexOfCurrent() + 1) % _queue.Count;
        PlayTrack(_queue[next], _queue);
    }

    private int IndexOfCurrent()
    {
        if (CurrentTrack is null) return -1;
        for (var index = 0; index < _queue.Count; index++)
            if (_queue[index].Id == CurrentTrack.Id) return index;
        return -1;
    }

    private int NextRandomIndex()
    {
        var current = IndexOfCurrent();
        var next = current;
        while (next == current) next = _random.Next(_queue.Count);
        return next;
    }

    private void OnPositionTick(object? sender, EventArgs e)
    {
        _updatingPosition = true;
        PositionSeconds = _player.Position.TotalSeconds;
        _updatingPosition = false;
        DurationSeconds = _player.Duration.TotalSeconds;
        IsPlaying = _player.IsPlaying;
    }

    private void PlayerOnPlaybackEnded(object? sender, EventArgs e) =>
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (Repeat && CurrentTrack is not null) PlayTrack(CurrentTrack, _queue);
            else if (Shuffle && _queue.Count > 1) Next();
            else
            {
                var currentIndex = IndexOfCurrent();
                if (currentIndex >= 0 && currentIndex < _queue.Count - 1) PlayTrack(_queue[currentIndex + 1], _queue);
                else IsPlaying = false;
            }
        });

    private void PlayerOnSpectrumAvailable(object? sender, float[] values) =>
        Application.Current.Dispatcher.BeginInvoke(() => SpectrumValues = values);

    private void RefreshCommands()
    {
        PlayPauseCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        _player.PlaybackEnded -= PlayerOnPlaybackEnded;
        _player.SpectrumAvailable -= PlayerOnSpectrumAvailable;
        GC.SuppressFinalize(this);
    }
}
