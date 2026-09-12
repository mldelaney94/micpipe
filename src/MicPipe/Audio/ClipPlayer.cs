using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicPipe.Audio;

/// <summary>
/// Pull-based clip player feeding the mix bus. Loads PCM into memory for low-latency hotkeys.
/// </summary>
public sealed class ClipPlayer : ISampleProvider, IDisposable
{
    private readonly WaveFormat _format;
    private readonly object _gate = new();
    private float[]? _samples;
    private int _position;
    private bool _playing;
    private float _volume = 1f;

    public ClipPlayer(WaveFormat format)
    {
        _format = format;
    }

    public WaveFormat WaveFormat => _format;

    public void SetVolume(float volume) => _volume = Math.Clamp(volume, 0f, 1f);

    public void PlayFile(string path)
    {
        using var reader = new AudioFileReader(path);
        var converted = Convert(reader, _format);
        lock (_gate)
        {
            _samples = converted;
            _position = 0;
            _playing = true;
        }
    }

    public void PlayPcm(float[] samples)
    {
        lock (_gate)
        {
            _samples = samples;
            _position = 0;
            _playing = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _playing = false;
            _position = 0;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            Array.Clear(buffer, offset, count);
            if (!_playing || _samples is null)
            {
                return count;
            }

            var remaining = _samples.Length - _position;
            if (remaining <= 0)
            {
                _playing = false;
                return count;
            }

            var toCopy = Math.Min(count, remaining);
            for (var i = 0; i < toCopy; i++)
            {
                buffer[offset + i] = _samples[_position + i] * _volume;
            }

            _position += toCopy;
            if (_position >= _samples.Length)
            {
                _playing = false;
            }

            return count;
        }
    }

    private static float[] Convert(AudioFileReader reader, WaveFormat target)
    {
        ISampleProvider samples = reader;
        if (samples.WaveFormat.Channels != target.Channels)
        {
            samples = target.Channels == 2
                ? new MonoToStereoSampleProvider(samples)
                : new StereoToMonoSampleProvider(samples);
        }

        if (samples.WaveFormat.SampleRate != target.SampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, target.SampleRate);
        }

        var list = new List<float>(capacity: (int)(reader.Length / 2));
        var buf = new float[target.SampleRate * target.Channels];
        int read;
        while ((read = samples.Read(buf, 0, buf.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                list.Add(buf[i]);
            }
        }

        return list.ToArray();
    }

    public void Dispose() => Stop();
}
