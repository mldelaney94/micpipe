using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicPipe.Audio;

/// <summary>
/// Clip source for the mix bus. Decodes a whole file into memory so hotkeys start instantly,
/// mirrors whatever it plays into <see cref="Hearback"/> (local speakers), and can withhold
/// the audio from the bus so a clip can be auditioned locally without reaching the cable.
/// Always fills the whole buffer (silence when idle) so the mixer never drops it as an input.
/// </summary>
public sealed class ClipPlayer : ISampleProvider
{
    private readonly object _gate = new();
    private float[] _samples = [];
    private int _position;
    private bool _playing;
    private bool _toBus;
    private float _volume = 1f;

    public ClipPlayer(WaveFormat format)
    {
        WaveFormat = format;
    }

    public WaveFormat WaveFormat { get; }

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Receives a copy of every played sample; cleared on <see cref="Stop"/>.</summary>
    public BufferedWaveProvider? Hearback { get; set; }

    /// <summary>Raised on the audio thread when a clip runs to its end. Not raised by <see cref="Stop"/>.</summary>
    public event EventHandler? PlaybackEnded;

    public bool IsPlaying
    {
        get { lock (_gate) return _playing; }
    }

    /// <summary>0–1 through the current clip, or null when idle.</summary>
    public double? GetProgress()
    {
        lock (_gate)
        {
            return _playing ? (double)_position / Math.Max(1, _samples.Length) : null;
        }
    }

    /// <param name="toBus">False = local hearback only; the bus receives silence.</param>
    public void Play(string path, bool toBus = true) => Play(Decode(path, WaveFormat), toBus);

    public void Play(float[] samples, bool toBus = true)
    {
        lock (_gate)
        {
            _samples = samples;
            _position = 0;
            _playing = true;
            _toBus = toBus;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _playing = false;
            _position = 0;
            _samples = [];
        }

        Hearback?.ClearBuffer();
    }

    public int Read(Span<float> buffer)
    {
        var ended = false;
        lock (_gate)
        {
            buffer.Clear();
            if (!_playing)
            {
                return buffer.Length;
            }

            var n = Math.Min(buffer.Length, _samples.Length - _position);
            for (var i = 0; i < n; i++)
            {
                buffer[i] = _samples[_position + i] * _volume;
            }

            _position += n;
            if (_position >= _samples.Length)
            {
                _playing = false;
                ended = true;
            }

            if (n > 0)
            {
                Hearback?.AddSamples(MemoryMarshal.AsBytes(buffer[..n]));
            }

            if (!_toBus)
            {
                buffer.Clear();
            }
        }

        if (ended)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        return buffer.Length;
    }

    /// <summary>Whole file as interleaved IEEE float in <paramref name="target"/>'s channel count and rate.</summary>
    public static float[] Decode(string path, WaveFormat target)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        if (source.WaveFormat.Channels != target.Channels)
        {
            source = target.Channels == 2
                ? new MonoToStereoSampleProvider(source)
                : new StereoToMonoSampleProvider(source);
        }

        if (source.WaveFormat.SampleRate != target.SampleRate)
        {
            source = new WdlResamplingSampleProvider(source, target.SampleRate);
        }

        var all = new List<float>();
        var chunk = new float[target.SampleRate * target.Channels];
        int read;
        while ((read = source.Read(chunk)) > 0)
        {
            all.AddRange(chunk.AsSpan(0, read));
        }

        return all.ToArray();
    }
}
