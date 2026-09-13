using Microsoft.UI.Xaml;
using MicPipe.Data;
using MicPipe.Services;
using MicPipe.Views;

namespace MicPipe;

public partial class App : Application
{
    private Window? _mainWindow;
    private bool _exitRequested;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            AppLog.Write("Unhandled: " + e.Message, e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        _mainWindow = new MainWindow();
        _mainWindow.Closed += MainWindow_Closed;
        _mainWindow.Activate();

        AppServices.Tray.Initialize(_mainWindow, ShowMain, Quit);
        AppServices.Hotkeys.Start();
        AppServices.Engine.StartIfConfigured();

        if (AppServices.Settings.NeedsDeviceSetup)
        {
            WindowHub.OpenDevices();
        }
    }

    /// <summary>Closing the main window hides it; the tray owns the app's lifetime.</summary>
    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_exitRequested)
        {
            return;
        }

        args.Handled = true;
        ((Window)sender).AppWindow.Hide();
    }

    private void ShowMain()
    {
        _mainWindow?.AppWindow.Show();
        _mainWindow?.Activate();
    }

    public void Quit()
    {
        _exitRequested = true;
        AppServices.Shutdown();
        _mainWindow?.Close();
        Exit();
    }
}
