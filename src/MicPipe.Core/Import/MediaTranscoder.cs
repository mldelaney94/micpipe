using System.Globalization;

namespace MicPipe.Import;

public static class MediaTranscoder
{
    /// <summary>Cuts [start, end) out of any ffmpeg-readable file into a 48 kHz stereo 16-bit WAV, applying linear gain.</summary>
    public static async Task<string> TrimToWavAsync(
        string inputPath,
        TimeSpan start,
        TimeSpan end,
        double gain = 1.0,
        CancellationToken ct = default)
    {
        if (end <= start)
        {
            throw new ArgumentException("End must be after start.");
        }

        var outputPath = Path.Combine(Path.GetTempPath(), "MicPipe", Guid.NewGuid().ToString("N") + ".wav");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var args = new List<string> { "-y", "-ss", Format(start), "-i", inputPath, "-t", Format(end - start) };
        gain = Math.Clamp(gain, 0.05, 4.0);
        if (Math.Abs(gain - 1.0) > 0.001)
        {
            args.AddRange(["-af", "volume=" + gain.ToString("0.###", CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", outputPath]);

        await ProcessRunner.RunAsync(Tools.Ffmpeg, args, "Couldn't process that audio file.", ct).ConfigureAwait(false);
        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("Couldn't process that audio file.");
        }

        return outputPath;
    }

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
