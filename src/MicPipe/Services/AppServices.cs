using MicPipe.Audio;
using MicPipe.Data;
using MicPipe.Hotkeys;

namespace MicPipe.Services;

/// <summary>Process-wide singletons, created once at launch.</summary>
public static class AppServices
{
    public static AppSettings Settings { get; private set; } = null!;
    public static ClipLibrary Library { get; private set; } = null!;
    public static AudioEngine Engine { get; private set; } = null!;
    public static HotkeyService Hotkeys { get; private set; } = null!;
    public static TrayIconService Tray { get; private set; } = null!;

    public static void Initialize()
    {
        Settings = AppSettings.Load();
        Library = ClipLibrary.Load();
        Engine = new AudioEngine(Settings, new PushToTalkService(Settings));
        Hotkeys = new HotkeyService(Settings, Library, Engine);
        Tray = new TrayIconService();
    }

    public static void Shutdown()
    {
        try { Hotkeys.Dispose(); } catch { /* best effort */ }
        try { Tray.Dispose(); } catch { /* best effort */ }
        try { Engine.Dispose(); } catch { /* best effort */ }
        Settings.Save();
    }
}
