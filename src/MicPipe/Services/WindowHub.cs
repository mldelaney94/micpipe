using Microsoft.UI.Xaml;
using MicPipe.Views;

namespace MicPipe.Services;

public static class WindowHub
{
    public static Window? Main { get; set; }

    private static ImportWindow? _import;
    private static TrimWindow? _trim;
    private static HotkeysWindow? _hotkeys;
    private static DevicesWindow? _devices;
    private static LibraryWindow? _library;

    public static void OpenImport()
    {
        _import ??= new ImportWindow();
        _import.Closed += (_, _) => _import = null;
        _import.Activate();
    }

    public static void OpenTrim(string sourcePath, string? suggestedName = null)
    {
        _trim ??= new TrimWindow();
        _trim.Closed += (_, _) => _trim = null;
        _trim.LoadSource(sourcePath, suggestedName);
        _trim.Activate();
    }

    public static void OpenHotkeys(string? focusClipId = null)
    {
        _hotkeys ??= new HotkeysWindow();
        _hotkeys.Closed += (_, _) => _hotkeys = null;
        _hotkeys.Refresh(focusClipId);
        _hotkeys.Activate();
    }

    public static void OpenDevices()
    {
        _devices ??= new DevicesWindow();
        _devices.Closed += (_, _) => _devices = null;
        _devices.Activate();
    }

    public static void OpenLibrary()
    {
        _library ??= new LibraryWindow();
        _library.Closed += (_, _) => _library = null;
        _library.Refresh();
        _library.Activate();
    }
}
