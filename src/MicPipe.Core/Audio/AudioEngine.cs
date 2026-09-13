using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MicPipe.Data;
using MicPipe.Services;

namespace MicPipe.Audio;

public sealed record AudioDeviceInfo(string Id, string Name, bool LooksLikeVirtualCable);

/// <summary>What the engine is currently playing; published through <see cref="AudioEngine.ClipChanged"/>.</summary>
public sealed record ActiveClip(string Path, string Name);

/// <summary>
/// Signal graph while live:
/// <code>
///   mic (WASAPI, converted to MixFormat) ── volume ──┐
///                                                    ├── mixer ── cable device
///   ClipPlayer ─────────────────────────────────────┘
///        └── hearback buffer ── monitor or default speakers
/// </code>
/// Holds the push-to-talk key from the moment a clip starts on the cable until shortly after it ends.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    /// <summary>Everything is converted to this before mixing.</summary>
    public static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>WASAPI latency for cable and hearback. Lower stutters on virtual cables.</summary>
    public const int OutputLatencyMs = 100;

    /// <summary>Covers the output buffer so the game hears the clip's tail before PTT lifts.</summary>
    private static readonly TimeSpan PttReleaseDelay = TimeSpan.FromMilliseconds(300);

    private readonly AppSettings _settings;
    private readonly PushToTalkService _ptt;
    private readonly object _gate = new();
    private WasapiRecorder? _mic;
    private VolumeSampleProvider? _micVolume;
    private ClipPlayer? _clips;
    private WasapiPlayer? _cableOut;
    private WasapiPlayer? _hearbackOut;
    private BufferedWaveProvider? _hearback;
    private int _pttGeneration;

    public event EventHandler? StateChanged;
    public event EventHandler<ActiveClip?>? ClipChanged;

    public bool IsLive { get; private set; }
    public ActiveClip? Current { get; private set; }
    public bool IsClipPlaying => _clips?.IsPlaying ?? false;
    public double? GetClipProgress() => _clips?.GetProgress();

    /// <summary>Bytes still queued for local speakers (0 after <see cref="StopClip"/>).</summary>
    public int HearbackBufferedBytes => _hearback?.BufferedBytes ?? 0;

    public AudioEngine(AppSettings settings, PushToTalkService ptt)
    {
        _settings = settings;
        _ptt = ptt;
    }

    public static IReadOnlyList<AudioDeviceInfo> ListCaptureDevices() => ListDevices(DataFlow.Capture);
    public static IReadOnlyList<AudioDeviceInfo> ListRenderDevices() => ListDevices(DataFlow.Render);

    public static bool IsCableName(string name) =>
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase);

    public static bool HasVirtualCableInstalled() =>
        ListRenderDevices().Concat(ListCaptureDevices()).Any(d => d.LooksLikeVirtualCable);

    private static IReadOnlyList<AudioDeviceInfo> ListDevices(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, IsCableName(d.FriendlyName)))
            .OrderBy(d => d.Name)
            .ToList();
    }

    /// <summary>Starts if devices are configured; failures are logged, not thrown.</summary>
    public void StartIfConfigured()
    {
        if (_settings.NeedsDeviceSetup)
        {
            return;
        }

        try
        {
            Start();
        }
        catch (Exception ex)
        {
            AppLog.Write("AudioEngine start failed", ex);
            Stop();
        }
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsLive)
            {
                return;
            }

            if (_settings.NeedsDeviceSetup)
            {
                throw new InvalidOperationException("Devices are not configured.");
            }

            using var enumerator = new MMDeviceEnumerator();

            // Mic: WASAPI shared mode converts to MixFormat for us; buffer bridges push capture to the pull mixer.
            var micBuffer = new BufferedWaveProvider(MixFormat, TimeSpan.FromMilliseconds(250)) { DiscardOnBufferOverflow = true };
            _mic = new WasapiRecorderBuilder()
                .WithDevice(enumerator.GetDevice(_settings.MicDeviceId))
                .WithFormat(MixFormat)
                .WithBufferLength(50)
                .Build();
            _mic.DataAvailable += (buffer, _, _, _) => micBuffer.AddSamples(buffer);
            _micVolume = new VolumeSampleProvider(micBuffer.ToSampleProvider()) { Volume = _settings.MicVolume };

            _clips = new ClipPlayer(MixFormat) { Volume = _settings.ClipVolume };
            _clips.PlaybackEnded += OnClipEnded;
            OpenHearback(enumerator);

            var mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
            mixer.AddMixerInput(_micVolume);
            mixer.AddMixerInput(_clips);

            _cableOut = new WasapiPlayerBuilder()
                .WithDevice(enumerator.GetDevice(_settings.CableOutputDeviceId))
                .WithLatency(OutputLatencyMs)
                .Build();
            _cableOut.Init(mixer);
            _cableOut.Play();
            _mic.StartRecording();
            IsLive = true;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clips are mirrored to the monitor device, else default speakers — never to the cable itself.</summary>
    private void OpenHearback(MMDeviceEnumerator enumerator)
    {
        try
        {
            var device = string.IsNullOrWhiteSpace(_settings.MonitorDeviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(_settings.MonitorDeviceId);
            if (device.ID == _settings.CableOutputDeviceId)
            {
                return;
            }

            // ReadFully keeps the device streaming silence between clips so Stop can just clear the buffer.
            _hearback = new BufferedWaveProvider(MixFormat, TimeSpan.FromMilliseconds(750))
            {
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };
            _hearbackOut = new WasapiPlayerBuilder().WithDevice(device).WithLatency(OutputLatencyMs).Build();
            _hearbackOut.Init(_hearback);
            _hearbackOut.Play();
            _clips!.Hearback = _hearback;
        }
        catch (Exception ex)
        {
            AppLog.Write("Hearback unavailable", ex);
            _hearbackOut = null;
            _hearback = null;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            IsLive = false;
            Quiet(() => _mic?.StopRecording());
            Quiet(() => _mic?.Dispose());
            Quiet(() => _cableOut?.Stop());
            Quiet(() => _cableOut?.Dispose());
            Quiet(() => _hearbackOut?.Stop());
            Quiet(() => _hearbackOut?.Dispose());
            _mic = null;
            _micVolume = null;
            _clips = null;
            _cableOut = null;
            _hearbackOut = null;
            _hearback = null;
        }

        ReleasePtt();
        SetCurrent(null);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMicVolume(float volume)
    {
        _settings.MicVolume = Math.Clamp(volume, 0f, 1f);
        if (_micVolume is not null)
        {
            _micVolume.Volume = _settings.MicVolume;
        }

        _settings.Save();
    }

    public void SetClipVolume(float volume)
    {
        _settings.ClipVolume = Math.Clamp(volume, 0f, 1f);
        if (_clips is not null)
        {
            _clips.Volume = _settings.ClipVolume;
        }

        _settings.Save();
    }

    /// <summary>Plays into the cable (and local hearback) and holds PTT for the duration.</summary>
    public void PlayClip(string path, string name) => Play(path, name, toBus: true);

    /// <summary>Auditions on local speakers only: nothing reaches the cable and PTT stays up.</summary>
    public void PreviewClip(string path, string name) => Play(path, name, toBus: false);

    private void Play(string path, string name, bool toBus)
    {
        if (!IsLive)
        {
            StartIfConfigured();
        }

        var clips = _clips ?? throw new InvalidOperationException("Set up devices before playing clips.");
        clips.Play(path, toBus);
        if (toBus)
        {
            Interlocked.Increment(ref _pttGeneration);
            _ptt.Press();
        }
        else
        {
            ReleasePtt();
        }

        SetCurrent(new ActiveClip(path, name));
    }

    public void StopClip()
    {
        _clips?.Stop();
        ReleasePtt();
        SetCurrent(null);
    }

    private void OnClipEnded(object? sender, EventArgs e)
    {
        var generation = _pttGeneration;
        _ = Task.Delay(PttReleaseDelay).ContinueWith(_ =>
        {
            if (generation == _pttGeneration)
            {
                _ptt.Release();
            }
        });
        SetCurrent(null);
    }

    /// <summary>Releases now and invalidates any pending delayed release.</summary>
    private void ReleasePtt()
    {
        Interlocked.Increment(ref _pttGeneration);
        _ptt.Release();
    }

    private void SetCurrent(ActiveClip? clip)
    {
        Current = clip;
        ClipChanged?.Invoke(this, clip);
    }

    private static void Quiet(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // teardown is best-effort
        }
    }

    public void Dispose() => Stop();
}
