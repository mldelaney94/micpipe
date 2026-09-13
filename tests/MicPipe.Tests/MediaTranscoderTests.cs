using NAudio.Wave;
using MicPipe.Import;

namespace MicPipe.Tests;

/// <summary>Runs the real bundled ffmpeg; skips when the private tools aren't fetched.</summary>
public class MediaTranscoderTests
{
    private static bool FfmpegAvailable()
    {
        try
        {
            return File.Exists(Tools.Ffmpeg);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Trim_with_gain_increases_peak()
    {
        if (!FfmpegAvailable()) return;
        using var root = new TempAppRoot();
        var src = TestAudio.WriteToneWav(root.Root, seconds: 1.0, amplitude: 0.1f);

        var quiet = await MediaTranscoder.TrimToWavAsync(src, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var loud = await MediaTranscoder.TrimToWavAsync(src, TimeSpan.Zero, TimeSpan.FromSeconds(1), gain: 2.0);

        var quietPeak = TestAudio.Peak(quiet);
        var loudPeak = TestAudio.Peak(loud);
        Assert.True(quietPeak > 0.05f, $"expected audible quiet peak, got {quietPeak}");
        Assert.True(loudPeak > quietPeak * 1.5f, $"gain not applied: quiet={quietPeak}, loud={loudPeak}");
    }

    [Fact]
    public async Task Trim_respects_time_window()
    {
        if (!FfmpegAvailable()) return;
        using var root = new TempAppRoot();
        var src = TestAudio.WriteToneWav(root.Root, seconds: 3.0, amplitude: 0.2f);

        var trimmed = await MediaTranscoder.TrimToWavAsync(src, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        using var reader = new AudioFileReader(trimmed);
        Assert.InRange(reader.TotalTime.TotalSeconds, 0.9, 1.2);
    }
}
