using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MicPipe.Audio;
using MicPipe.Data;
using MicPipe.Services;

namespace MicPipe.Tests;

/// <summary>
/// End-to-end checks through a real VB-Audio CABLE: play into "CABLE Input", listen on "CABLE Output".
/// Each test returns early (passes) when the cable or a physical mic is absent.
/// Player + cable add roughly 200 ms of latency, so assertions look at time windows, not instants.
/// </summary>
public class CablePipelineTests
{
    private const int PipelineLatencyMs = 400;

    private sealed record Devices(string MicId, string CableInputId, string CableOutputId);

    private static Devices? ResolveDevices()
    {
        using var en = new MMDeviceEnumerator();
        var capture = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
        var render = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

        var mic = capture.FirstOrDefault(d =>
            !d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
            !d.FriendlyName.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase));
        var cableOut = capture.FirstOrDefault(d => d.FriendlyName.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase));
        var cableIn = render.FirstOrDefault(d =>
            d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) &&
            !d.FriendlyName.Contains("16ch", StringComparison.OrdinalIgnoreCase));

        return mic is null || cableIn is null || cableOut is null ? null : new Devices(mic.ID, cableIn.ID, cableOut.ID);
    }

    private static AudioEngine StartEngine(Devices devices)
    {
        var settings = new AppSettings
        {
            MicDeviceId = devices.MicId,
            CableOutputDeviceId = devices.CableInputId,
            MicVolume = 0f, // keep room noise out of the measurements
            ClipVolume = 1f
        };
        var engine = new AudioEngine(settings, new PushToTalkService(settings));
        engine.Start();
        return engine;
    }

    /// <summary>Records a device and keeps the peak of every buffer with its arrival time.</summary>
    private sealed class PeakTimeline : IDisposable
    {
        private readonly WasapiRecorder _recorder;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<(long Ms, float Peak)> _points = new();

        public PeakTimeline(string deviceId)
        {
            using var en = new MMDeviceEnumerator();
            _recorder = new WasapiRecorderBuilder().WithDevice(en.GetDevice(deviceId)).Build();
            _recorder.DataAvailable += (buffer, _, _, _) =>
            {
                var peak = TestAudio.Peak(buffer, _recorder.WaveFormat);
                lock (_points) _points.Add((_clock.ElapsedMilliseconds, peak));
            };
            _recorder.StartRecording();
        }

        public long Now => _clock.ElapsedMilliseconds;

        public float PeakBetween(long fromMs, long toMs)
        {
            lock (_points)
            {
                return _points.Where(p => p.Ms >= fromMs && p.Ms < toMs).Select(p => p.Peak).DefaultIfEmpty(0f).Max();
            }
        }

        /// <summary>Peak per fixed-size window, in order.</summary>
        public List<float> Windows(int windowMs)
        {
            lock (_points)
            {
                return _points.GroupBy(p => p.Ms / windowMs).OrderBy(g => g.Key).Select(g => g.Max(p => p.Peak)).ToList();
            }
        }

        public void Dispose()
        {
            _recorder.StopRecording();
            _recorder.Dispose();
        }
    }

    [Fact]
    public void PlayClip_reaches_cable_output_and_StopClip_cuts_it()
    {
        if (ResolveDevices() is not Devices devices) return;
        using var root = new TempAppRoot();
        using var engine = StartEngine(devices);
        Assert.True(engine.IsLive);
        var wav = TestAudio.WriteToneWav(root.Root, seconds: 3, amplitude: 0.4f, hz: 880);

        using var cable = new PeakTimeline(devices.CableOutputId);
        Thread.Sleep(300);
        var tPlay = cable.Now;
        engine.PlayClip(wav, "tone");
        Thread.Sleep(900);
        var tStop = cable.Now;
        engine.StopClip();
        Thread.Sleep(PipelineLatencyMs + 500);

        var playing = cable.PeakBetween(tPlay + PipelineLatencyMs, tStop);
        var afterStop = cable.PeakBetween(tStop + PipelineLatencyMs, cable.Now);
        Assert.True(playing > 0.2f, $"clip did not reach CABLE Output (peak={playing})");
        Assert.True(afterStop < playing * 0.1f, $"StopClip did not silence cable (play={playing}, after={afterStop})");
    }

    [Fact]
    public void PreviewClip_stays_off_the_cable()
    {
        if (ResolveDevices() is not Devices devices) return;
        using var root = new TempAppRoot();
        using var engine = StartEngine(devices);
        var wav = TestAudio.WriteToneWav(root.Root, seconds: 1.5, amplitude: 0.4f, hz: 880);

        using var cable = new PeakTimeline(devices.CableOutputId);
        Thread.Sleep(300);
        var tPlay = cable.Now;
        engine.PreviewClip(wav, "tone");
        Assert.NotNull(engine.Current);
        Thread.Sleep(900);

        var leaked = cable.PeakBetween(tPlay, cable.Now);
        Assert.True(leaked < 0.02f, $"preview leaked onto the cable (peak={leaked})");
        engine.StopClip();
    }

    [Fact]
    public void PlayClip_queues_hearback_and_StopClip_clears_it()
    {
        if (ResolveDevices() is not Devices devices) return;
        using var root = new TempAppRoot();
        using var engine = StartEngine(devices);
        var wav = TestAudio.WriteToneWav(root.Root, seconds: 1.5, amplitude: 0.3f, hz: 880);

        engine.PlayClip(wav, "tone");
        Thread.Sleep(250);
        Assert.True(engine.IsClipPlaying);
        Assert.True(engine.HearbackBufferedBytes > 0, "hearback should hold clip PCM while playing");

        engine.StopClip();
        Assert.False(engine.IsClipPlaying);
        Assert.Equal(0, engine.HearbackBufferedBytes);
    }

    [Fact]
    public void PlayClip_cable_stream_has_no_long_dropout_gaps()
    {
        if (ResolveDevices() is not Devices devices) return;
        using var root = new TempAppRoot();
        using var engine = StartEngine(devices);
        var wav = TestAudio.WriteToneWav(root.Root, seconds: 2.0, amplitude: 0.35f, hz: 880);

        using var cable = new PeakTimeline(devices.CableOutputId);
        Thread.Sleep(300);
        engine.PlayClip(wav, "tone");
        Thread.Sleep(1500);
        engine.StopClip();

        // A dropout shows up as a near-silent 50 ms window in the middle of an otherwise steady tone.
        var active = cable.Windows(50).SkipWhile(p => p < 0.02f).ToList();
        if (active.Count < 8) return; // not enough signal observed to judge
        var mid = active.Skip(2).SkipLast(2).ToList();
        var silent = mid.Count(p => p < 0.015f);
        var ratio = silent / (double)mid.Count;
        Assert.True(ratio < 0.15, $"cable stream stuttered: {silent}/{mid.Count} silent 50ms windows (ratio={ratio:F2})");
    }
}
