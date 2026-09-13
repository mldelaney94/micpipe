using NAudio.Wave;

namespace MicPipe.Tests;

/// <summary>Shared tone generation and peak measurement for the audio tests.</summary>
internal static class TestAudio
{
    public static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>Interleaved stereo sine at <paramref name="hz"/>.</summary>
    public static float[] Tone(double seconds, float amplitude, double hz = 440)
    {
        var frames = (int)(Format.SampleRate * seconds);
        var samples = new float[frames * Format.Channels];
        for (var f = 0; f < frames; f++)
        {
            var v = amplitude * (float)Math.Sin(2 * Math.PI * hz * f / Format.SampleRate);
            samples[2 * f] = v;
            samples[2 * f + 1] = v;
        }

        return samples;
    }

    public static string WriteToneWav(string dir, double seconds, float amplitude, double hz = 440)
    {
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".wav");
        using var writer = new WaveFileWriter(path, Format);
        var samples = Tone(seconds, amplitude, hz);
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    public static float Peak(string path)
    {
        using var reader = new AudioFileReader(path);
        float peak = 0;
        var buf = new float[4096];
        int read;
        while ((read = reader.Read(buf)) > 0)
        {
            peak = Math.Max(peak, Peak(buf.AsSpan(0, read)));
        }

        return peak;
    }

    public static float Peak(ReadOnlySpan<float> samples)
    {
        float peak = 0;
        foreach (var s in samples)
        {
            peak = Math.Max(peak, Math.Abs(s));
        }

        return peak;
    }

    /// <summary>Peak of a raw WASAPI capture buffer in either float or 16-bit PCM.</summary>
    public static float Peak(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        float peak = 0;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat || format.BitsPerSample == 32)
        {
            for (var i = 0; i + 4 <= buffer.Length; i += 4)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(buffer.Slice(i, 4))));
            }
        }
        else
        {
            for (var i = 0; i + 2 <= buffer.Length; i += 2)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(buffer.Slice(i, 2)) / 32768f));
            }
        }

        return peak;
    }
}
