using System.Diagnostics;
using DownloaderV2.Models;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
    public event EventHandler<AudioFrame>? AudioFrameAvailable;

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
            var analysisProvider = new AudioAnalysisSampleProvider(_volumeProvider);
            analysisProvider.AudioFrameAvailable += (_, frame) => AudioFrameAvailable?.Invoke(this, frame);
            _output = new WaveOut();
            _output.PlaybackStopped += OutputOnPlaybackStopped;
            _output.Init(analysisProvider.ToWaveProvider());
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
        AudioFrameAvailable?.Invoke(this, AudioFrame.Empty);
    }

    public void Stop()
    {
        lock (_sync)
        {
            _manualStop = true;
            _output?.Stop();
            if (_reader is not null) _reader.CurrentTime = TimeSpan.Zero;
        }
        AudioFrameAvailable?.Invoke(this, AudioFrame.Empty);
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
        AudioFrameAvailable?.Invoke(this, AudioFrame.Empty);
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

    private sealed class AudioAnalysisSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private const int FftLength = 2048;
        private const int SpectrumBars = 64;
        private const int WaveformSamples = 256;
        private readonly Complex[] _fftBuffer = new Complex[FftLength];
        private readonly float[] _waveformRing = new float[WaveformSamples];
        private readonly float[] _smoothedSpectrum = new float[SpectrumBars];
        private readonly int _channels = Math.Max(1, source.WaveFormat.Channels);
        private int _fftPosition;
        private int _waveformPosition;
        private int _channelIndex;
        private float _channelSum;
        private float _leftSample;
        private float _rightSample;
        private double _squareSum;
        private double _leftSquareSum;
        private double _rightSquareSum;
        private float _peak;
        private long _sequence;
        private float _bass;
        private float _lowMid;
        private float _mid;
        private float _highMid;
        private float _treble;

        public event EventHandler<AudioFrame>? AudioFrameAvailable;
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var samplesRead = source.Read(buffer.AsSpan(offset, count));
            ProcessSamples(buffer.AsSpan(offset, samplesRead));
            return samplesRead;
        }

        public int Read(Span<float> buffer)
        {
            var samplesRead = source.Read(buffer);
            ProcessSamples(buffer[..samplesRead]);
            return samplesRead;
        }

        private void ProcessSamples(ReadOnlySpan<float> samples)
        {
            foreach (var sample in samples)
            {
                if (_channelIndex == 0) _leftSample = sample;
                if (_channelIndex == 1) _rightSample = sample;
                _channelSum += sample;
                _channelIndex++;
                if (_channelIndex < _channels) continue;

                if (_channels == 1) _rightSample = _leftSample;
                AddAudioSample(_channelSum / _channels, _leftSample, _rightSample);
                _channelIndex = 0;
                _channelSum = 0;
            }
        }

        private void AddAudioSample(float mono, float left, float right)
        {
            _waveformRing[_waveformPosition] = mono;
            _waveformPosition = (_waveformPosition + 1) % WaveformSamples;
            _squareSum += mono * mono;
            _leftSquareSum += left * left;
            _rightSquareSum += right * right;
            _peak = Math.Max(_peak, Math.Abs(mono));

            _fftBuffer[_fftPosition].X = mono * (float)FastFourierTransform.HammingWindow(_fftPosition, FftLength);
            _fftBuffer[_fftPosition].Y = 0;
            _fftPosition++;
            if (_fftPosition < FftLength) return;

            _fftPosition = 0;
            FastFourierTransform.FFT(true, 11, _fftBuffer);
            PublishFrame();
        }

        private void PublishFrame()
        {
            var spectrum = CreateSpectrum();
            _bass = Smooth(_bass, Average(spectrum, 0, 8));
            _lowMid = Smooth(_lowMid, Average(spectrum, 8, 17));
            _mid = Smooth(_mid, Average(spectrum, 17, 32));
            _highMid = Smooth(_highMid, Average(spectrum, 32, 48));
            _treble = Smooth(_treble, Average(spectrum, 48, SpectrumBars));

            var waveform = new float[WaveformSamples];
            for (var index = 0; index < WaveformSamples; index++)
                waveform[index] = _waveformRing[(_waveformPosition + index) % WaveformSamples];

            var frame = new AudioFrame(
                ++_sequence,
                waveform,
                spectrum,
                _bass,
                _lowMid,
                _mid,
                _highMid,
                _treble,
                (float)Math.Sqrt(_squareSum / FftLength),
                _peak,
                (float)Math.Sqrt(_leftSquareSum / FftLength),
                (float)Math.Sqrt(_rightSquareSum / FftLength));

            _squareSum = 0;
            _leftSquareSum = 0;
            _rightSquareSum = 0;
            _peak = 0;
            AudioFrameAvailable?.Invoke(this, frame);
        }

        private float[] CreateSpectrum()
        {
            var result = new float[SpectrumBars];
            var usableBins = FftLength / 2;
            for (var bar = 0; bar < SpectrumBars; bar++)
            {
                var start = Math.Max(1, (int)Math.Pow(usableBins, bar / (double)SpectrumBars));
                var end = Math.Min(usableBins, Math.Max(start + 1, (int)Math.Pow(usableBins, (bar + 1d) / SpectrumBars)));
                var peak = 0f;
                for (var bin = start; bin < end; bin++)
                {
                    var magnitude = MathF.Sqrt(_fftBuffer[bin].X * _fftBuffer[bin].X + _fftBuffer[bin].Y * _fftBuffer[bin].Y);
                    peak = Math.Max(peak, magnitude);
                }
                var raw = Math.Clamp(MathF.Log10(1 + peak * 35), 0, 1);
                var factor = raw > _smoothedSpectrum[bar] ? 0.62f : 0.18f;
                _smoothedSpectrum[bar] += (raw - _smoothedSpectrum[bar]) * factor;
                result[bar] = _smoothedSpectrum[bar];
            }
            return result;
        }

        private static float Average(float[] values, int start, int end)
        {
            var total = 0f;
            for (var index = start; index < end; index++) total += values[index];
            return total / Math.Max(1, end - start);
        }

        private static float Smooth(float current, float target) =>
            current + (target - current) * (target > current ? 0.58f : 0.16f);
    }
}
