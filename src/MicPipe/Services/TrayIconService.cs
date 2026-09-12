using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using MicPipe.Data;

namespace MicPipe.Services;

public sealed class TrayIconService : IDisposable
{
    private NotifyIconData _data;
    private bool _added;
    private Action? _onShow;
    private Action? _onQuit;
    private Window? _window;

    public void Initialize(Window window, Action onShow, Action onQuit)
    {
        _window = window;
        _onShow = onShow;
        _onQuit = onQuit;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _data = new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmAppTray,
            hIcon = LoadIcon(IntPtr.Zero, IdiApplication),
            szTip = "MicPipe"
        };

        _added = Shell_NotifyIcon(NimAdd, ref _data);

        // Subclass via a hidden message-only window would be ideal; for v1 use window WndProc hook.
        SubclassWindow(hwnd);
    }

    private IntPtr _oldWndProc = IntPtr.Zero;
    private WndProcDelegate? _proc;

    private void SubclassWindow(IntPtr hwnd)
    {
        _proc = TrayWndProc;
        _oldWndProc = SetWindowLongPtr(hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_proc));
    }

    private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmAppTray)
        {
            var mouse = (uint)lParam.ToInt64() & 0xFFFF;
            if (mouse is WmLButtonDblClk or WmLButtonUp)
            {
                _onShow?.Invoke();
            }
            else if (mouse == WmRButtonUp)
            {
                ShowContextMenu(hWnd);
            }

            return IntPtr.Zero;
        }

        if (msg == WmCommand)
        {
            var id = wParam.ToInt32() & 0xFFFF;
            if (id == 1001) _onShow?.Invoke();
            if (id == 1002) _onQuit?.Invoke();
            return IntPtr.Zero;
        }

        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr hwnd)
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, 1001, "Open MicPipe");
        AppendMenu(menu, MfString, 1002, "Quit");
        GetCursorPos(out var pt);
        SetForegroundWindow(hwnd);
        TrackPopupMenu(menu, TpmRightButton, pt.X, pt.Y, 0, hwnd, IntPtr.Zero);
        DestroyMenu(menu);
    }

    public void Dispose()
    {
        if (_added)
        {
            Shell_NotifyIcon(NimDelete, ref _data);
            _added = false;
        }
    }

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint WmAppTray = 0x8001;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmCommand = 0x0111;
    private const int GwlpWndProc = -4;
    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private static readonly IntPtr IdiApplication = new(32512);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
}
