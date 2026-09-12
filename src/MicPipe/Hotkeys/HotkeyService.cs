using System.Runtime.InteropServices;
using MicPipe.Audio;
using MicPipe.Data;

namespace MicPipe.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmApp = 0x8000;
    private const int WmReloadBindings = WmApp + 32;
    private const int WhMouseLl = 14;
    private const int WmXbuttonDown = 0x020B;
    private const int Xbutton1 = 0x0001;
    private const int Xbutton2 = 0x0002;

    private readonly AppSettings _settings;
    private readonly ClipLibrary _library;
    private readonly AudioEngine _engine;
    private readonly Dictionary<int, string> _idToClip = new();
    private readonly Dictionary<int, string> _mouseButtonToClip = new(); // 4/5 -> clipId
    private IntPtr _hwnd;
    private IntPtr _mouseHook;
    private Thread? _thread;
    private volatile bool _running;
    private WndProc? _wndProc;
    private LowLevelMouseProc? _mouseProc;
    private IntPtr _wndProcPtr;

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

        MigrateLegacyBindings();
        _running = true;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "MicPipe.Hotkeys"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void ReloadBindings()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // RegisterHotKey/UnregisterHotKey must run on the thread that owns _hwnd.
        if (!PostMessage(_hwnd, WmReloadBindings, IntPtr.Zero, IntPtr.Zero))
        {
            AppLog.Write("PostMessage reload hotkeys failed: " + Marshal.GetLastWin32Error());
        }
    }

    public void BindClip(string clipId, int code, bool isMouse, string label)
    {
        // One bind per clip, one clip per key
        _settings.ClipKeybinds.RemoveAll(b =>
            b.ClipId == clipId ||
            (b.IsMouse == isMouse && b.Code == code));

        _settings.ClipKeybinds.Add(new ClipKeybind
        {
            ClipId = clipId,
            Code = code,
            IsMouse = isMouse,
            Label = label
        });
        _settings.Save();
        ReloadBindings();
    }

    public void ClearClipBind(string clipId)
    {
        _settings.ClipKeybinds.RemoveAll(b => b.ClipId == clipId);
        _settings.Save();
        ReloadBindings();
    }

    private void MigrateLegacyBindings()
    {
        if (_settings.ClipKeybinds.Count > 0 || _settings.HotkeyBindings.Count == 0)
        {
            return;
        }

        foreach (var pair in _settings.HotkeyBindings)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (!TryResolveVirtualKey(pair.Key, out var vk))
            {
                continue;
            }

            _settings.ClipKeybinds.Add(new ClipKeybind
            {
                ClipId = pair.Value,
                Code = (int)vk,
                IsMouse = false,
                Label = pair.Key
            });
        }

        _settings.Save();
    }

    private void MessageLoop()
    {
        _wndProc = WndProcHandler;
        _wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProc);
        _mouseProc = MouseHookCallback;

        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = _wndProcPtr,
            hInstance = GetModuleHandle(null),
            lpszClassName = "MicPipeHotkeyWindow"
        };

        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            AppLog.Write("RegisterClassEx failed: " + Marshal.GetLastWin32Error());
            return;
        }

        _hwnd = CreateWindowEx(0, "MicPipeHotkeyWindow", "MicPipeHotkey", 0,
            0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        RegisterAll();

        while (_running)
        {
            var ret = GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (ret == 0 || ret == -1)
            {
                break;
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnregisterAll();
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr WndProcHandler(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmReloadBindings)
        {
            UnregisterAll();
            RegisterAll();
            return IntPtr.Zero;
        }

        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            if (_idToClip.TryGetValue(id, out var clipId))
            {
                PlayClipId(clipId);
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WmXbuttonDown)
        {
            var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            var xBtn = (int)((info.mouseData >> 16) & 0xffff);
            var button = xBtn == Xbutton1 ? 4 : xBtn == Xbutton2 ? 5 : 0;
            if (button != 0 && _mouseButtonToClip.TryGetValue(button, out var clipId))
            {
                PlayClipId(clipId);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void PlayClipId(string clipId)
    {
        try
        {
            if (_library.TryGet(clipId, out var entry) && entry is not null)
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
        _idToClip.Clear();
        _mouseButtonToClip.Clear();
        var id = 1;
        var needMouseHook = false;
        var registered = 0;

        foreach (var bind in _settings.ClipKeybinds)
        {
            if (string.IsNullOrWhiteSpace(bind.ClipId) || bind.Code <= 0)
            {
                continue;
            }

            if (bind.IsMouse)
            {
                _mouseButtonToClip[bind.Code] = bind.ClipId;
                needMouseHook = true;
                continue;
            }

            SetLastError(0);
            if (RegisterHotKey(_hwnd, id, 0, (uint)bind.Code))
            {
                _idToClip[id] = bind.ClipId;
                registered++;
                id++;
            }
            else
            {
                AppLog.Write($"RegisterHotKey failed for {bind.Label} (vk={bind.Code}): " + Marshal.GetLastWin32Error());
            }
        }

        // Legacy F-row dictionary still supported if not migrated somehow
        foreach (var pair in _settings.HotkeyBindings)
        {
            if (string.IsNullOrWhiteSpace(pair.Value) || !TryResolveVirtualKey(pair.Key, out var vk))
            {
                continue;
            }

            if (_settings.ClipKeybinds.Any(b => !b.IsMouse && b.Code == (int)vk))
            {
                continue;
            }

            SetLastError(0);
            if (RegisterHotKey(_hwnd, id, 0, vk))
            {
                _idToClip[id] = pair.Value;
                registered++;
                id++;
            }
        }

        AppLog.Write($"Hotkeys registered: {registered} key(s), {_mouseButtonToClip.Count} mouse.");

        if (needMouseHook && _mouseHook == IntPtr.Zero && _mouseProc is not null)
        {
            _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero)
            {
                AppLog.Write("SetWindowsHookEx mouse failed: " + Marshal.GetLastWin32Error());
            }
        }
        else if (!needMouseHook && _mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private void UnregisterAll()
    {
        foreach (var hotkeyId in _idToClip.Keys.ToList())
        {
            UnregisterHotKey(_hwnd, hotkeyId);
        }

        // Belt-and-braces: clear any orphaned ids if a prior cross-thread reload left them behind
        for (var i = 1; i <= 64; i++)
        {
            UnregisterHotKey(_hwnd, i);
        }

        _idToClip.Clear();
        _mouseButtonToClip.Clear();

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    public static bool TryResolveVirtualKey(string name, out uint vk)
    {
        vk = 0;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name[1..], out var n) && n is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + (n - 1));
            return true;
        }

        if (Enum.TryParse<Windows.System.VirtualKey>(name, ignoreCase: true, out var parsed) &&
            parsed != Windows.System.VirtualKey.None)
        {
            vk = (uint)parsed;
            return true;
        }

        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z')
            {
                vk = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        _running = false;
        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(1000);
    }

    private static readonly IntPtr HWND_MESSAGE = new(-3);

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

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint dwErrCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
