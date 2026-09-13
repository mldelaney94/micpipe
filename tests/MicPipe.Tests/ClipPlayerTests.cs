using NAudio.Wave;
using MicPipe.Audio;

namespace MicPipe.Tests;

public class ClipPlayerTests
{
    [Fact]
    public void Plays_and_reports_progress()
    {
        var player = new ClipPlayer(TestAudio.Format);
        player.Play(TestAudio.Tone(0.5, 0.25f));

        Assert.True(player.IsPlaying);
        var buf = new float[960];
        Assert.Equal(buf.Length, player.Read(buf));
        Assert.True(TestAudio.Peak(buf) > 0.01f);
        Assert.True(player.GetProgress() is > 0 and < 1);
    }

    [Fact]
    public void Idle_player_still_fills_buffer_with_silence()
    {
        var player = new ClipPlayer(TestAudio.Format);
        var buf = new float[4800];
        buf.AsSpan().Fill(1f);

        Assert.Equal(buf.Length, player.Read(buf));
        Assert.All(buf, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Stop_silences_immediately_and_clears_progress()
    {
        var player = new ClipPlayer(TestAudio.Format);
        player.Play(TestAudio.Tone(2, 0.25f));
        _ = player.Read(new float[4800]);

        player.Stop();

        Assert.False(player.IsPlaying);
        Assert.Null(player.GetProgress());
        var buf = new float[4800];
        player.Read(buf);
        Assert.All(buf, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void PlaybackEnded_fires_once_at_natural_end_only()
    {
        var player = new ClipPlayer(TestAudio.Format);
        var ends = 0;
        player.PlaybackEnded += (_, _) => ends++;

        player.Play(TestAudio.Tone(0.1, 0.25f));
        player.Stop();
        Assert.Equal(0, ends);

        player.Play(TestAudio.Tone(0.1, 0.25f));
        var buf = new float[TestAudio.Format.SampleRate * 2]; // a full second, longer than the clip
        player.Read(buf);
        player.Read(buf);
        Assert.Equal(1, ends);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void Preview_mode_keeps_bus_silent_but_feeds_hearback()
    {
        var player = new ClipPlayer(TestAudio.Format)
        {
            Hearback = new BufferedWaveProvider(TestAudio.Format, TimeSpan.FromSeconds(1))
        };
        player.Play(TestAudio.Tone(0.5, 0.25f), toBus: false);

        var buf = new float[960];
        player.Read(buf);

        Assert.All(buf, s => Assert.Equal(0f, s));
        Assert.Equal(buf.Length * sizeof(float), player.Hearback.BufferedBytes);

        player.Stop();
        Assert.Equal(0, player.Hearback.BufferedBytes);
    }

    [Fact]
    public void Volume_scales_output()
    {
        var player = new ClipPlayer(TestAudio.Format) { Volume = 0.5f };
        player.Play(TestAudio.Tone(0.5, 0.8f));

        var buf = new float[4800];
        player.Read(buf);
        Assert.InRange(TestAudio.Peak(buf), 0.35f, 0.41f);
    }
}
