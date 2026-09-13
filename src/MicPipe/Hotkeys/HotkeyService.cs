using System.Runtime.InteropServices;
using MicPipe.Audio;
using MicPipe.Data;

namespace MicPipe.Hotkeys;

/// <summary>
/// Global clip triggers from <see cref="AppSettings.ClipKeybinds"/>: keyboard keys via RegisterHotKey on a
/// message-only window (own STA thread), side mouse buttons via a low-level mouse hook on that same thread.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const int WmReloadBindings = 0x8000 + 32; // WM_APP + 32
    private const int WhMouseLl = 14;
    private const int WmXbuttonDown = 0x020B;
    private const int Xbutton1 = 0x0001;
    private const int Xbutton2 = 0x0002;

    private readonly AppSettings _settings;
    private readonly ClipLibrary _library;
    private readonly AudioEngine _engine;
    private readonly Dictionary<int, string> _hotkeyIdToClip = new();
    private readonly Dictionary<int, string> _mouseButtonToClip = new();
    private IntPtr _hwnd;
    private IntPtr _mouseHook;
    private Thread? _thread;
    private volatile bool _running;
    private WndProc? _wndProc;
    private LowLevelMouseProc? _mouseProc;

    public HotkeyService(AppSettings settings, ClipLibrary library, AudioEngine engine)
    {
        _settings = settings;
        _library = library;
        _engine = engine;
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "MicPipe.Hotkeys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Re-registers from settings. Marshalled to the hotkey thread, which owns the window.</summary>
    public void ReloadBindings()
    {
        if (_hwnd != IntPtr.Zero && !PostMessage(_hwnd, WmReloadBindings, IntPtr.Zero, IntPtr.Zero))
        {
            AppLog.Write("PostMessage reload hotkeys failed: " + Marshal.GetLastWin32Error());
        }
    }

    /// <summary>One bind per clip, one clip per key.</summary>
    public void BindClip(string clipId, int code, bool isMouse, string label)
    {
        _settings.ClipKeybinds.RemoveAll(b => b.ClipId == clipId || (b.IsMouse == isMouse && b.Code == code));
        _settings.ClipKeybinds.Add(new ClipKeybind { ClipId = clipId, Code = code, IsMouse = isMouse, Label = label });
        _settings.Save();
        ReloadBindings();
    }

    public void ClearClipBind(string clipId)
    {
        _settings.ClipKeybinds.RemoveAll(b => b.ClipId == clipId);
        _settings.Save();
        ReloadBindings();
    }

    private void MessageLoop()
    {
        _wndProc = WndProcHandler;
        _mouseProc = MouseHookCallback;

        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "MicPipeHotkeyWindow"
        };
        if (RegisterClassEx(ref wc) == 0)
        {
            AppLog.Write("RegisterClassEx failed: " + Marshal.GetLastWin32Error());
            return;
        }

        _hwnd = CreateWindowEx(0, wc.lpszClassName, "MicPipeHotkey", 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        RegisterAll();

        while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnregisterAll();
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WndProcHandler(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmReloadBindings:
                UnregisterAll();
                RegisterAll();
                return IntPtr.Zero;
            case WmHotkey:
                if (_hotkeyIdToClip.TryGetValue(wParam.ToInt32(), out var clipId))
                {
                    PlayClip(clipId);
                }

                return IntPtr.Zero;
            default:
                return DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WmXbuttonDown)
        {
            var xButton = (int)((Marshal.PtrToStructure<MsllHookStruct>(lParam).mouseData >> 16) & 0xffff);
            var button = xButton == Xbutton1 ? 4 : xButton == Xbutton2 ? 5 : 0;
            if (_mouseButtonToClip.TryGetValue(button, out var clipId))
            {
                PlayClip(clipId);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void PlayClip(string clipId)
    {
        try
        {
            if (_library.Find(clipId) is ClipEntry entry)
            {
                _engine.PlayClip(_library.GetPath(entry), entry.Name);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Hotkey play failed", ex);
        }
    }

    private void RegisterAll()
    {
        var nextId = 1;
        foreach (var bind in _settings.ClipKeybinds.Where(b => !string.IsNullOrWhiteSpace(b.ClipId) && b.Code > 0))
        {
            if (bind.IsMouse)
            {
                _mouseButtonToClip[bind.Code] = bind.ClipId;
            }
            else if (RegisterHotKey(_hwnd, nextId, 0, (uint)bind.Code))
            {
                _hotkeyIdToClip[nextId++] = bind.ClipId;
            }
            else
            {
                AppLog.Write($"RegisterHotKey failed for {bind.Label} (vk={bind.Code}): " + Marshal.GetLastWin32Error());
            }
        }

        AppLog.Write($"Hotkeys registered: {_hotkeyIdToClip.Count} key(s), {_mouseButtonToClip.Count} mouse.");

        if (_mouseButtonToClip.Count > 0)
        {
            _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc!, GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero)
            {
                AppLog.Write("SetWindowsHookEx mouse failed: " + Marshal.GetLastWin32Error());
            }
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _hotkeyIdToClip.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }

        _hotkeyIdToClip.Clear();
        _mouseButtonToClip.Clear();

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        _running = false;
        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(1000);
    }

    private static readonly IntPtr HwndMessage = new(-3);

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public int ptX;
        public int ptY;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
