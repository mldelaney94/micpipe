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
            AppLog.Write("Unhandled", e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        _mainWindow = new MainWindow();
        WindowHub.Main = _mainWindow;
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

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_exitRequested)
        {
            return;
        }

        // Hide instead of quit — tray owns lifetime.
        args.Handled = true;
        if (sender is Window window)
        {
            window.AppWindow.Hide();
        }
    }

    private void ShowMain()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.AppWindow.Show();
        _mainWindow.Activate();
    }

    public void Quit()
    {
        _exitRequested = true;
        AppServices.Shutdown();
        _mainWindow?.Close();
        Exit();
    }
}
