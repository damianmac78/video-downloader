using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace DownloaderV2.Services;

public sealed class AudioPlayerService : IDisposable
{
    private readonly object _sync = new();
    private WaveOut? _output;
    private AudioFileReader? _reader;
    private VolumeSampleProvider? _volumeProvider;
    private bool _manualStop;
    private float _volume = 0.8f;

    public event EventHandler? PlaybackEnded;
    public event EventHandler<float[]>? SpectrumAvailable;

    public bool IsLoaded => _reader is not null;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_volumeProvider is not null) _volumeProvider.Volume = _volume;
        }
    }

    public void Load(string filePath)
    {
        lock (_sync)
        {
            DisposePlayback();
            _reader = new AudioFileReader(filePath);
            _volumeProvider = new VolumeSampleProvider(_reader) { Volume = _volume };
            var spectrumProvider = new SpectrumSampleProvider(_volumeProvider);
            spectrumProvider.SpectrumAvailable += (_, values) => SpectrumAvailable?.Invoke(this, values);

            _output = new WaveOut();
            _output.PlaybackStopped += OutputOnPlaybackStopped;
            _output.Init(spectrumProvider.ToWaveProvider());
            _manualStop = false;
        }
    }

    public void Play()
    {
        lock (_sync)
        {
            _manualStop = false;
            _output?.Play();
        }
    }

    public void Pause()
    {
        lock (_sync) _output?.Pause();
    }

    public void Stop()
    {
        lock (_sync)
        {
            _manualStop = true;
            _output?.Stop();
            if (_reader is not null) _reader.CurrentTime = TimeSpan.Zero;
        }
        SpectrumAvailable?.Invoke(this, []);
    }

    public void Seek(TimeSpan position)
    {
        lock (_sync)
        {
            if (_reader is null) return;
            _reader.CurrentTime = position < TimeSpan.Zero
                ? TimeSpan.Zero
                : position > _reader.TotalTime ? _reader.TotalTime : position;
        }
    }

    private void OutputOnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null) Debug.WriteLine(e.Exception);
        var endedNaturally = !_manualStop && _reader is not null &&
                             _reader.TotalTime - _reader.CurrentTime <= TimeSpan.FromMilliseconds(300);
        SpectrumAvailable?.Invoke(this, []);
        if (endedNaturally) PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void DisposePlayback()
    {
        if (_output is not null)
        {
            _manualStop = true;
            _output.Stop();
            _output.PlaybackStopped -= OutputOnPlaybackStopped;
            _output.Dispose();
        }
        _reader?.Dispose();
        _output = null;
        _reader = null;
        _volumeProvider = null;
    }

    public void Dispose()
    {
        lock (_sync) DisposePlayback();
        GC.SuppressFinalize(this);
    }

    private sealed class SpectrumSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private const int FftLength = 2048;
        private const int SpectrumBars = 64;
        private readonly Complex[] _fftBuffer = new Complex[FftLength];
        private int _fftPosition;

        public event EventHandler<float[]>? SpectrumAvailable;
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var samplesRead = source.Read(buffer.AsSpan(offset, count));
            for (var index = 0; index < samplesRead; index++) AddSample(buffer[offset + index]);
            return samplesRead;
        }

        public int Read(Span<float> buffer)
        {
            var samplesRead = source.Read(buffer);
            for (var index = 0; index < samplesRead; index++) AddSample(buffer[index]);
            return samplesRead;
        }

        private void AddSample(float sample)
        {
            _fftBuffer[_fftPosition].X = sample * (float)FastFourierTransform.HammingWindow(_fftPosition, FftLength);
            _fftBuffer[_fftPosition].Y = 0;
            _fftPosition++;
            if (_fftPosition < FftLength) return;

            _fftPosition = 0;
            FastFourierTransform.FFT(true, 11, _fftBuffer);
            SpectrumAvailable?.Invoke(this, CreateSpectrum());
        }

        private float[] CreateSpectrum()
        {
            var values = new float[SpectrumBars];
            var usableBins = FftLength / 2;
            for (var bar = 0; bar < SpectrumBars; bar++)
            {
                var start = Math.Max(1, (int)Math.Pow(usableBins, bar / (double)SpectrumBars));
                var end = Math.Max(start + 1, (int)Math.Pow(usableBins, (bar + 1d) / SpectrumBars));
                end = Math.Min(end, usableBins);

                var peak = 0f;
                for (var bin = start; bin < end; bin++)
                {
                    var magnitude = MathF.Sqrt(_fftBuffer[bin].X * _fftBuffer[bin].X + _fftBuffer[bin].Y * _fftBuffer[bin].Y);
                    peak = Math.Max(peak, magnitude);
                }
                values[bar] = Math.Clamp(MathF.Log10(1 + peak * 35), 0, 1);
            }
            return values;
        }
    }
}
