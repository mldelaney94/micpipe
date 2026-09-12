using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MicPipe.Data;

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
    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private WasapiOut? _monitor;
    private BufferedWaveProvider? _micBuffer;
    private VolumeSampleProvider? _micVolume;
    private ClipPlayer? _clipPlayer;
    private WaveFormat? _mixFormat;
    private bool _running;

    public event EventHandler? StateChanged;
    public event EventHandler<string?>? ClipChanged;

    public bool IsLive => _running;
    public string? LastClipName { get; private set; }

    public AudioEngine(AppSettings settings)
    {
        _settings = settings;
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
        if (!_running || _clipPlayer is null)
        {
            StartIfConfigured();
        }

        if (_clipPlayer is null)
        {
            return;
        }

        _clipPlayer.PlayFile(path);
        LastClipName = displayName;
        ClipChanged?.Invoke(this, displayName);
    }

    public void StopClip()
    {
        _clipPlayer?.Stop();
        LastClipName = null;
        ClipChanged?.Invoke(this, null);
    }

    public void Dispose() => Stop();
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
