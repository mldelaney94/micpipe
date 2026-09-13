using Microsoft.UI.Xaml;
using MicPipe.Views;

namespace MicPipe.Services;

/// <summary>One instance of each tool window at a time; reopening brings the existing one forward.</summary>
public static class WindowHub
{
    private static readonly Slot<ImportWindow> Import = new();
    private static readonly Slot<TrimWindow> Trim = new();
    private static readonly Slot<HotkeysWindow> Hotkeys = new();
    private static readonly Slot<DevicesWindow> Devices = new();
    private static readonly Slot<LibraryWindow> Library = new();

    public static void OpenImport() => Import.Show(() => new ImportWindow());
    public static void OpenDevices() => Devices.Show(() => new DevicesWindow());
    public static void OpenLibrary() => Library.Show(() => new LibraryWindow()).Refresh();
    public static void OpenHotkeys() => Hotkeys.Show(() => new HotkeysWindow()).Refresh();
    public static void OpenTrim(TrimRequest request) => Trim.Show(() => new TrimWindow()).Load(request);

    public static void RefreshHotkeysIfOpen() => Hotkeys.Current?.Refresh();

    private sealed class Slot<T> where T : Window
    {
        public T? Current { get; private set; }

        public T Show(Func<T> create)
        {
            if (Current is null)
            {
                Current = create();
                Current.Closed += (_, _) => Current = null;
            }

            Current.Activate();
            return Current;
        }
    }
}
