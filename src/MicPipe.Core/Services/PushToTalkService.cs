using System.Runtime.InteropServices;
using MicPipe.Data;

namespace MicPipe.Services;

/// <summary>Holds the configured push-to-talk key or mouse button via SendInput while clips play.</summary>
public sealed class PushToTalkService
{
    private readonly AppSettings _settings;
    private readonly object _gate = new();
    private bool _held;
    private int _code;
    private bool _isMouse;

    public PushToTalkService(AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsConfigured => _settings.PushToTalkVirtualKey is > 0;

    /// <summary>Presses (re-pressing if already held so every clip gets a fresh key-down). No-op when unconfigured.</summary>
    public void Press()
    {
        lock (_gate)
        {
            if (_settings.PushToTalkVirtualKey is not int code || code <= 0)
            {
                return;
            }

            if (_held)
            {
                Send(down: false);
            }

            _code = code;
            _isMouse = _settings.PushToTalkIsMouse;
            Send(down: true);
            _held = true;
            AppLog.Write($"PTT down: {(_isMouse ? "Mouse" : "Key")} {_code} ({_settings.PushToTalkKeyName})");
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            if (!_held)
            {
                return;
            }

            Send(down: false);
            _held = false;
            AppLog.Write($"PTT up: {(_isMouse ? "Mouse" : "Key")} {_code}");
        }
    }

    private void Send(bool down)
    {
        if (_isMouse)
        {
            SendMouse(_code, down);
        }
        else
        {
            SendKey(_code, down);
        }
    }

    /// <summary>Sends both a VK and a scancode event; games differ in which one they read.</summary>
    private static void SendKey(int virtualKey, bool down)
    {
        var scan = (ushort)MapVirtualKey((uint)virtualKey, MapVkToVsc);
        var upFlag = down ? 0u : KeyeventfKeyup;
        var scanFlags = KeyeventfScancode | upFlag | (IsExtendedKey(virtualKey) ? KeyeventfExtendedKey : 0);

        Span<Input> inputs = stackalloc Input[2];
        inputs[0] = new Input { type = InputKeyboard, U = new InputUnion { ki = new KeybdInput { wVk = (ushort)virtualKey, wScan = scan, dwFlags = upFlag } } };
        inputs[1] = new Input { type = InputKeyboard, U = new InputUnion { ki = new KeybdInput { wVk = 0, wScan = scan, dwFlags = scanFlags } } };

        var sent = SendInput(2, ref inputs[0], Marshal.SizeOf<Input>());
        if (sent != 2)
        {
            AppLog.Write($"SendInput key failed (sent={sent}, err={Marshal.GetLastWin32Error()})");
        }
    }

    private static bool IsExtendedKey(int virtualKey) =>
        virtualKey is (>= 0x21 and <= 0x28) // page/end/home/arrows
            or 0x2D or 0x2E // insert/delete
            or 0x5B or 0x5C // win
            or 0xA3 or 0xA5; // right ctrl/alt

    /// <param name="mouseButton">3 = middle, 4 = XButton1 (Mouse4), 5 = XButton2 (Mouse5)</param>
    private static void SendMouse(int mouseButton, bool down)
    {
        var (flags, data) = mouseButton switch
        {
            3 => (down ? MouseeventfMiddleDown : MouseeventfMiddleUp, 0u),
            4 => (down ? MouseeventfXDown : MouseeventfXUp, Xbutton1),
            5 => (down ? MouseeventfXDown : MouseeventfXUp, Xbutton2),
            _ => (0u, 0u)
        };
        if (flags == 0)
        {
            return;
        }

        var input = new Input { type = InputMouse, U = new InputUnion { mi = new MouseInput { mouseData = data, dwFlags = flags } } };
        var sent = SendInput(1, ref input, Marshal.SizeOf<Input>());
        if (sent != 1)
        {
            AppLog.Write($"SendInput mouse failed (sent={sent}, err={Marshal.GetLastWin32Error()})");
        }
    }

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfExtendedKey = 0x0001;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;
    private const uint MapVkToVsc = 0;
    private const uint MouseeventfMiddleDown = 0x0020;
    private const uint MouseeventfMiddleUp = 0x0040;
    private const uint MouseeventfXDown = 0x0080;
    private const uint MouseeventfXUp = 0x0100;
    private const uint Xbutton1 = 0x0001;
    private const uint Xbutton2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, ref Input pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
