using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace MicPipe.Views;

internal static class WindowSetup
{
    /// <summary>Initial size, minimum size and title-bar icon. Purely window-chrome; each window styles its own content.</summary>
    public static void Apply(Window window, int width, int height, int minWidth, int minHeight)
    {
        window.AppWindow.Resize(new SizeInt32(width, height));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = minWidth;
            presenter.PreferredMinimumHeight = minHeight;
        }

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "MicPipe.ico");
        if (File.Exists(icon))
        {
            window.AppWindow.SetIcon(icon);
        }
    }
}
