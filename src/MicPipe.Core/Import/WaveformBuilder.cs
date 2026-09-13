using NAudio.Wave;

namespace MicPipe.Import;

public static class WaveformBuilder
{
    /// <summary>Peak amplitude per bucket, normalised to 0–1, for drawing a waveform.</summary>
    public static float[] BuildPeaks(string path, int bucketCount = 400)
    {
        using var reader = new AudioFileReader(path);
        var peaks = new float[bucketCount];
        var totalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
        if (totalSamples <= 0)
        {
            return peaks;
        }

        var samplesPerBucket = Math.Max(1, totalSamples / bucketCount);
        var chunk = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        long index = 0;
        int read;
        while ((read = reader.Read(chunk)) > 0)
        {
            for (var i = 0; i < read; i++, index++)
            {
                var bucket = (int)Math.Min(bucketCount - 1, index / samplesPerBucket);
                peaks[bucket] = Math.Max(peaks[bucket], Math.Abs(chunk[i]));
            }
        }

        var max = peaks.Max();
        if (max > 0)
        {
            for (var i = 0; i < peaks.Length; i++)
            {
                peaks[i] /= max;
            }
        }

        return peaks;
    }

    public static TimeSpan GetDuration(string path)
    {
        using var reader = new AudioFileReader(path);
        return reader.TotalTime;
    }
}
