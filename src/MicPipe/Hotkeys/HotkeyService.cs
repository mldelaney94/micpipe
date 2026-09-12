using System.Runtime.InteropServices;
using MicPipe.Audio;
using MicPipe.Data;

namespace MicPipe.Hotkeys;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly AppSettings _settings;
    private readonly ClipLibrary _library;
    private readonly AudioEngine _engine;
    private readonly Dictionary<int, string> _idToClip = new();
    private IntPtr _hwnd;
    private Thread? _thread;
    private volatile bool _running;
    private WndProc? _wndProc;
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

        UnregisterAll();
        RegisterAll();
    }

    private void MessageLoop()
    {
        _wndProc = WndProcHandler;
        _wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProc);

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
        if (msg == WmHotkey)
        {
            var id = wParam.ToInt32();
            if (_idToClip.TryGetValue(id, out var clipId) &&
                _library.TryGet(clipId, out var entry) &&
                entry is not null)
            {
                try
                {
                    if (_settings.HotkeyHoldToPlay)
                    {
                        // Press fires play; release isn't delivered via RegisterHotKey alone.
                        // Hold mode falls back to press-to-play for v1 RegisterHotKey.
                    }

                    _engine.PlayClip(_library.GetPath(entry), entry.Name);
                }
                catch (Exception ex)
                {
                    AppLog.Write("Hotkey play failed", ex);
                }
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void RegisterAll()
    {
        _idToClip.Clear();
        var id = 1;
        foreach (var pair in _settings.HotkeyBindings)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (!TryParseFunctionKey(pair.Key, out var vk))
            {
                continue;
            }

            if (RegisterHotKey(_hwnd, id, 0, vk))
            {
                _idToClip[id] = pair.Value;
                id++;
            }
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _idToClip.Keys.ToList())
        {
            UnregisterHotKey(_hwnd, id);
        }

        _idToClip.Clear();
    }

    public static bool TryParseFunctionKey(string name, out uint vk)
    {
        vk = 0;
        if (!name.StartsWith("F", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(name[1..], out var n) || n is < 1 or > 12)
        {
            return false;
        }

        vk = (uint)(0x70 + (n - 1)); // VK_F1 = 0x70
        return true;
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
}
