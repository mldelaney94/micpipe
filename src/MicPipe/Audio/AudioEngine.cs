using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MicPipe.Data;
using MicPipe.Services;

namespace MicPipe.Audio;

public sealed class AudioDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool LooksLikeVirtualCable { get; init; }
}

public sealed class AudioEngine : IDisposable
{
    private readonly AppSettings _settings;
    private readonly PushToTalkService _ptt;
    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private WasapiOut? _monitor;
    private WasapiOut? _previewOut;
    private AudioFileReader? _previewReader;
    private BufferedWaveProvider? _micBuffer;
    private VolumeSampleProvider? _micVolume;
    private ClipPlayer? _clipPlayer;
    private WaveFormat? _mixFormat;
    private bool _running;
    private int _pttHoldGeneration;
    private CancellationTokenSource? _pttHoldCts;

    public event EventHandler? StateChanged;
    public event EventHandler<string?>? ClipChanged;

    public bool IsLive => _running;
    public string? LastClipName { get; private set; }

    /// <summary>0–1 playback progress for the active clip/preview, or null if idle.</summary>
    public double? GetClipProgress()
    {
        // Prefer cable clip progress when live (local hearback may also be open).
        var cableProgress = _clipPlayer?.GetProgress();
        if (cableProgress is not null)
        {
            return cableProgress;
        }

        if (_previewOut is not null && _previewReader is not null)
        {
            var total = _previewReader.TotalTime.TotalSeconds;
            if (total <= 0) return 0;
            return Math.Clamp(_previewReader.CurrentTime.TotalSeconds / total, 0, 1);
        }

        return null;
    }

    public bool IsClipPlaying =>
        (_previewOut is not null && _previewOut.PlaybackState == PlaybackState.Playing) ||
        (_clipPlayer?.IsPlaying ?? false);

    public AudioEngine(AppSettings settings, PushToTalkService ptt)
    {
        _settings = settings;
        _ptt = ptt;
    }

    public static IReadOnlyList<AudioDeviceInfo> ListCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDeviceInfo
            {
                Id = d.ID,
                Name = d.FriendlyName,
                LooksLikeVirtualCable = IsCableName(d.FriendlyName)
            })
            .OrderBy(d => d.Name)
            .ToList();
    }

    public static IReadOnlyList<AudioDeviceInfo> ListRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDeviceInfo
            {
                Id = d.ID,
                Name = d.FriendlyName,
                LooksLikeVirtualCable = IsCableName(d.FriendlyName)
            })
            .OrderBy(d => d.Name)
            .ToList();
    }

    public static bool IsCableName(string name) =>
        name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase);

    public static bool HasVirtualCableInstalled() =>
        ListRenderDevices().Any(d => d.LooksLikeVirtualCable) ||
        ListCaptureDevices().Any(d => d.LooksLikeVirtualCable);

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
            AppLog.Write("AudioEngine.StartIfConfigured failed", ex);
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
            if (_running)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.MicDeviceId) ||
                string.IsNullOrWhiteSpace(_settings.CableOutputDeviceId))
            {
                throw new InvalidOperationException("Devices are not configured.");
            }

            using var enumerator = new MMDeviceEnumerator();
            var mic = enumerator.GetDevice(_settings.MicDeviceId);
            var cable = enumerator.GetDevice(_settings.CableOutputDeviceId);

            _capture = new WasapiCapture(mic);
            _micBuffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(250)
            };
            _capture.DataAvailable += (_, e) =>
            {
                _micBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            var sampleRate = _capture.WaveFormat.SampleRate;
            _mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

            ISampleProvider micSamples = _micBuffer.ToSampleProvider();
            if (micSamples.WaveFormat.Channels == 1)
            {
                micSamples = new MonoToStereoSampleProvider(micSamples);
            }
            else if (micSamples.WaveFormat.Channels > 2)
            {
                micSamples = new StereoToMonoSampleProvider(micSamples);
                micSamples = new MonoToStereoSampleProvider(micSamples);
            }

            if (micSamples.WaveFormat.SampleRate != sampleRate)
            {
                micSamples = new WdlResamplingSampleProvider(micSamples, sampleRate);
            }

            _micVolume = new VolumeSampleProvider(micSamples) { Volume = _settings.MicVolume };

            _clipPlayer = new ClipPlayer(_mixFormat);
            _clipPlayer.SetVolume(_settings.ClipVolume);
            _clipPlayer.PlaybackEnded += ClipPlayer_PlaybackEnded;

            var mixer = new MixingSampleProvider(_mixFormat) { ReadFully = true };
            mixer.AddMixerInput(_micVolume);
            mixer.AddMixerInput(_clipPlayer);

            ISampleProvider outputSource = mixer;
            BufferedWaveProvider? monitorBuffer = null;
            if (!string.IsNullOrWhiteSpace(_settings.MonitorDeviceId) &&
                !string.Equals(_settings.MonitorDeviceId, _settings.CableOutputDeviceId, StringComparison.Ordinal))
            {
                monitorBuffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2))
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(300)
                };
                outputSource = new MonitorTeeSampleProvider(mixer, monitorBuffer);
            }

            _output = new WasapiOut(cable, AudioClientShareMode.Shared, true, 50);
            _output.Init(outputSource);
            _output.Play();

            if (monitorBuffer is not null)
            {
                try
                {
                    var monitorDevice = enumerator.GetDevice(_settings.MonitorDeviceId);
                    var monMixer = new MixingSampleProvider(_mixFormat) { ReadFully = true };
                    monMixer.AddMixerInput(monitorBuffer.ToSampleProvider());
                    _monitor = new WasapiOut(monitorDevice, AudioClientShareMode.Shared, true, 50);
                    _monitor.Init(monMixer);
                    _monitor.Play();
                }
                catch (Exception ex)
                {
                    AppLog.Write("Monitor device open failed", ex);
                }
            }

            _capture.StartRecording();
            _running = true;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        EndPttHold();
        lock (_gate)
        {
            _running = false;
            try { _capture?.StopRecording(); } catch { /* ignore */ }
            try { _capture?.Dispose(); } catch { /* ignore */ }
            try { _output?.Stop(); } catch { /* ignore */ }
            try { _output?.Dispose(); } catch { /* ignore */ }
            try { _monitor?.Stop(); } catch { /* ignore */ }
            try { _monitor?.Dispose(); } catch { /* ignore */ }
            try { _clipPlayer?.Dispose(); } catch { /* ignore */ }
            try { StopPreviewInternal(); } catch { /* ignore */ }
            _ptt.Release();
            _capture = null;
            _output = null;
            _monitor = null;
            _micBuffer = null;
            _micVolume = null;
            _clipPlayer = null;
            _mixFormat = null;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClipPlayer_PlaybackEnded(object? sender, EventArgs e)
    {
        // Do not release PTT here — BeginPttHold's duration timer (+ buffer slack) is
        // authoritative so games still hear the tail of the clip while PTT is down.
        try { StopPreviewInternal(); } catch { /* ignore */ }
        LastClipName = null;
        ClipChanged?.Invoke(this, null);
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
        _clipPlayer?.SetVolume(_settings.ClipVolume);
        _settings.Save();
    }

    public void PlayClip(string path, string displayName)
    {
        StopPreviewInternal();

        if (!_running || _clipPlayer is null)
        {
            StartIfConfigured();
        }

        if (_clipPlayer is null)
        {
            return;
        }

        TimeSpan duration;
        try
        {
            using var probe = new AudioFileReader(path);
            duration = probe.TotalTime;
        }
        catch
        {
            duration = TimeSpan.FromSeconds(2);
        }

        BeginPttHold(duration);
        _clipPlayer.PlayFile(path);
        StartLocalHearback(path);
        LastClipName = displayName;
        ClipChanged?.Invoke(this, displayName);
    }

    /// <summary>
    /// Play the clip on local speakers/headphones so you can hear what the cable is sending.
    /// Skipped when a full mix monitor is already running (you'd hear it twice).
    /// </summary>
    private void StartLocalHearback(string path)
    {
        if (_monitor is not null)
        {
            return;
        }

        try
        {
            StopPreviewInternal();
            _previewReader = new AudioFileReader(path) { Volume = _settings.ClipVolume };

            using var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            if (!string.IsNullOrWhiteSpace(_settings.MonitorDeviceId) &&
                !string.Equals(_settings.MonitorDeviceId, _settings.CableOutputDeviceId, StringComparison.Ordinal))
            {
                device = enumerator.GetDevice(_settings.MonitorDeviceId);
            }
            else
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            if (string.Equals(device.ID, _settings.CableOutputDeviceId, StringComparison.Ordinal))
            {
                AppLog.Write("Local hearback skipped: would play into the virtual cable.");
                _previewReader.Dispose();
                _previewReader = null;
                return;
            }

            _previewOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
            _previewOut.PlaybackStopped += (_, _) =>
            {
                // Do not touch PTT — cable playback owns hold duration.
                try { StopPreviewInternal(); } catch { /* ignore */ }
            };
            _previewOut.Init(_previewReader);
            _previewOut.Play();
        }
        catch (Exception ex)
        {
            AppLog.Write("Local hearback failed", ex);
            StopPreviewInternal();
        }
    }

    /// <summary>
    /// Preview on local speakers/headphones (and monitor if set), so the scrubber is audible.
    /// Still holds PTT if configured.
    /// </summary>
    public void PreviewClipLocal(string path, string displayName)
    {
        try
        {
            StopPreviewInternal();
            // Don't call ClipPlayer.Stop() here — that would fire PlaybackEnded and drop PTT early.
            // Mute/stop cable clip by swapping in silence via Stop without double-release:
            if (_clipPlayer is not null)
            {
                _clipPlayer.PlaybackEnded -= ClipPlayer_PlaybackEnded;
                _clipPlayer.Stop();
                _clipPlayer.PlaybackEnded += ClipPlayer_PlaybackEnded;
            }

            _previewReader = new AudioFileReader(path) { Volume = _settings.ClipVolume };
            var duration = _previewReader.TotalTime;

            using var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            if (!string.IsNullOrWhiteSpace(_settings.MonitorDeviceId))
            {
                device = enumerator.GetDevice(_settings.MonitorDeviceId);
            }
            else
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            _previewOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
            var genAtStart = _pttHoldGeneration;
            _previewOut.PlaybackStopped += (_, _) =>
            {
                if (genAtStart == _pttHoldGeneration)
                {
                    EndPttHold();
                }

                LastClipName = null;
                ClipChanged?.Invoke(this, null);
                StopPreviewInternal();
            };
            _previewOut.Init(_previewReader);
            BeginPttHold(duration);
            _previewOut.Play();
            LastClipName = displayName;
            ClipChanged?.Invoke(this, displayName);
        }
        catch (Exception ex)
        {
            AppLog.Write("PreviewClipLocal failed", ex);
            EndPttHold();
            StopPreviewInternal();
            throw;
        }
    }

    public void StopClip()
    {
        StopPreviewInternal();
        if (_clipPlayer is not null)
        {
            _clipPlayer.PlaybackEnded -= ClipPlayer_PlaybackEnded;
            _clipPlayer.Stop();
            _clipPlayer.PlaybackEnded += ClipPlayer_PlaybackEnded;
        }

        EndPttHold();
        LastClipName = null;
        ClipChanged?.Invoke(this, null);
    }

    private void BeginPttHold(TimeSpan clipDuration)
    {
        var gen = Interlocked.Increment(ref _pttHoldGeneration);
        _pttHoldCts?.Cancel();
        _pttHoldCts?.Dispose();
        _pttHoldCts = new CancellationTokenSource();
        var token = _pttHoldCts.Token;

        _ptt.Press();

        var holdFor = clipDuration + TimeSpan.FromMilliseconds(200);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(holdFor, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (gen == _pttHoldGeneration)
            {
                _ptt.Release();
            }
        }, token);
    }

    private void EndPttHold()
    {
        Interlocked.Increment(ref _pttHoldGeneration);
        _pttHoldCts?.Cancel();
        _ptt.Release();
    }

    private void StopPreviewInternal()
    {
        try { _previewOut?.Stop(); } catch { /* ignore */ }
        try { _previewOut?.Dispose(); } catch { /* ignore */ }
        try { _previewReader?.Dispose(); } catch { /* ignore */ }
        _previewOut = null;
        _previewReader = null;
        // PTT release is owned by EndPttHold / BeginPttHold — don't release here
        // when switching preview → cable play (BeginPttHold re-asserts).
    }

    public void Dispose()
    {
        EndPttHold();
        Stop();
    }
}

/// <summary>
/// Passes samples through to the cable while mirroring PCM into a monitor buffer.
/// </summary>
internal sealed class MonitorTeeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BufferedWaveProvider _monitor;
    private readonly byte[] _byteScratch = new byte[8192 * 4];

    public MonitorTeeSampleProvider(ISampleProvider source, BufferedWaveProvider monitor)
    {
        _source = source;
        _monitor = monitor;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read <= 0)
        {
            return read;
        }

        var byteCount = read * 4;
        if (byteCount > _byteScratch.Length)
        {
            // skip mirror for oversized chunk rather than allocate on audio thread
            return read;
        }

        Buffer.BlockCopy(buffer, offset * 4, _byteScratch, 0, byteCount);
        try
        {
            _monitor.AddSamples(_byteScratch, 0, byteCount);
        }
        catch
        {
            // overflow discarded via DiscardOnBufferOverflow
        }

        return read;
    }
}
