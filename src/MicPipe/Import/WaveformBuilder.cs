using NAudio.Wave;

namespace MicPipe.Import;

public static class WaveformBuilder
{
    public static float[] BuildPeaks(string path, int bucketCount = 400)
    {
        using var reader = new AudioFileReader(path);
        var samples = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        var peaks = new float[bucketCount];
        long totalSamples = (long)(reader.Length / (reader.WaveFormat.BitsPerSample / 8));
        if (totalSamples <= 0)
        {
            return peaks;
        }

        var samplesPerBucket = Math.Max(1, totalSamples / bucketCount);
        long sampleIndex = 0;
        int read;
        while ((read = reader.Read(samples, 0, samples.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var bucket = (int)Math.Min(bucketCount - 1, sampleIndex / samplesPerBucket);
                peaks[bucket] = Math.Max(peaks[bucket], Math.Abs(samples[i]));
                sampleIndex++;
            }
        }

        var max = peaks.DefaultIfEmpty(0).Max();
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
