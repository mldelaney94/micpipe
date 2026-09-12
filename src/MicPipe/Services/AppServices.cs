using MicPipe.Audio;
using MicPipe.Data;
using MicPipe.Hotkeys;
using MicPipe.Import;
using MicPipe.Services;

namespace MicPipe.Services;

public static class AppServices
{
    public static AppSettings Settings { get; private set; } = null!;
    public static ClipLibrary Library { get; private set; } = null!;
    public static AudioEngine Engine { get; private set; } = null!;
    public static HotkeyService Hotkeys { get; private set; } = null!;
    public static TrayIconService Tray { get; private set; } = null!;
    public static ToolResolver Tools { get; private set; } = null!;
    public static MediaTranscoder Transcoder { get; private set; } = null!;
    public static UrlAudioFetcher UrlFetcher { get; private set; } = null!;

    public static void Initialize()
    {
        Directory.CreateDirectory(AppSettings.RootDir);
        Settings = AppSettings.Load();
        Library = ClipLibrary.Load();
        Tools = new ToolResolver();
        Transcoder = new MediaTranscoder(Tools);
        UrlFetcher = new UrlAudioFetcher(Tools);
        var ptt = new PushToTalkService(Settings);
        Engine = new AudioEngine(Settings, ptt);
        Hotkeys = new HotkeyService(Settings, Library, Engine);
        Tray = new TrayIconService();
    }

    public static void Shutdown()
    {
        try { Hotkeys.Dispose(); } catch { /* ignore */ }
        try { Tray.Dispose(); } catch { /* ignore */ }
        try { Engine.Dispose(); } catch { /* ignore */ }
        Settings.Save();
    }
}
