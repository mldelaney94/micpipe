using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MicPipe.Views;

/// <summary>Turns raw key/pointer events into the (code, isMouse, label) triple stored for PTT and clip binds.</summary>
internal static class InputCapture
{
    /// <summary>3 = middle, 4 = Mouse4, 5 = Mouse5. Left/right return null so ordinary clicks never bind.</summary>
    public static int? BindableMouseButton(PointerRoutedEventArgs e, UIElement relativeTo) =>
        e.GetCurrentPoint(relativeTo).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.XButton1Pressed => 4,
            PointerUpdateKind.XButton2Pressed => 5,
            PointerUpdateKind.MiddleButtonPressed => 3,
            _ => null
        };

    public static bool IsModifier(VirtualKey key) => key is
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    /// <summary>"Number3" → "3"; everything else keeps the enum name.</summary>
    public static string KeyLabel(VirtualKey key)
    {
        var name = key.ToString();
        return name.Length == 7 && name.StartsWith("Number", StringComparison.Ordinal) ? name[6..] : name;
    }
}
