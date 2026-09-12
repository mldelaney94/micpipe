using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MicPipe.Audio;
using MicPipe.Services;
using Windows.System;

namespace MicPipe.Views;

public sealed partial class DevicesWindow : Window
{
    private bool _capturingPtt;
    private int? _pendingPttCode;
    private string? _pendingPttName;
    private bool _pendingPttIsMouse;

    public DevicesWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(672, 864));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 576;
            presenter.PreferredMinimumHeight = 672;
        }

        TrySetWindowIcon();
        Content.KeyDown += Content_KeyDown;
        Content.PointerPressed += Content_PointerPressed;
        Refresh();
        LoadPttUi();
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MicPipe.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // non-fatal
        }
    }

    private void LoadPttUi()
    {
        _pendingPttCode = AppServices.Settings.PushToTalkVirtualKey;
        _pendingPttName = AppServices.Settings.PushToTalkKeyName;
        _pendingPttIsMouse = AppServices.Settings.PushToTalkIsMouse;
        PttKeyBox.Text = string.IsNullOrWhiteSpace(_pendingPttName) ? "" : _pendingPttName;
    }

    private void PttCapture_Click(object sender, RoutedEventArgs e)
    {
        _capturingPtt = true;
        PttHint.Visibility = Visibility.Visible;
        PttCaptureButton.Content = "Waiting…";
        Content.Focus(FocusState.Programmatic);
    }

    private void PttClear_Click(object sender, RoutedEventArgs e)
    {
        _capturingPtt = false;
        _pendingPttCode = null;
        _pendingPttName = null;
        _pendingPttIsMouse = false;
        PttKeyBox.Text = "";
        PttHint.Visibility = Visibility.Collapsed;
        PttCaptureButton.Content = "Set";
    }

    private void Content_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturingPtt)
        {
            return;
        }

        e.Handled = true;
        if (e.Key is VirtualKey.Escape)
        {
            _capturingPtt = false;
            PttHint.Visibility = Visibility.Collapsed;
            PttCaptureButton.Content = "Set";
            return;
        }

        FinishPttCapture(isMouse: false, code: (int)e.Key, name: e.Key.ToString());
    }

    private void Content_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_capturingPtt)
        {
            return;
        }

        var kind = e.GetCurrentPoint(Content).Properties.PointerUpdateKind;
        int? button = kind switch
        {
            PointerUpdateKind.XButton1Pressed => 4,
            PointerUpdateKind.XButton2Pressed => 5,
            PointerUpdateKind.MiddleButtonPressed => 3,
            _ => null
        };

        if (button is null)
        {
            return;
        }

        e.Handled = true;
        FinishPttCapture(isMouse: true, code: button.Value, name: "Mouse" + button.Value);
    }

    private void FinishPttCapture(bool isMouse, int code, string name)
    {
        _capturingPtt = false;
        PttHint.Visibility = Visibility.Collapsed;
        PttCaptureButton.Content = "Set";
        _pendingPttIsMouse = isMouse;
        _pendingPttCode = code;
        _pendingPttName = name;
        PttKeyBox.Text = name;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var capture = AudioEngine.ListCaptureDevices().ToList();
        var render = AudioEngine.ListRenderDevices().ToList();

        var hasCable = AudioEngine.HasVirtualCableInstalled();
        CableStatus.Text = hasCable
            ? "Virtual cable detected."
            : "Virtual cable not detected. Install VB-Audio Virtual Cable, then Refresh.";

        MicCombo.ItemsSource = capture;
        CableCombo.ItemsSource = render;
        var monitorItems = new List<AudioDeviceInfo>
        {
            new() { Id = "", Name = "— none (use default speakers for preview) —", LooksLikeVirtualCable = false }
        };
        monitorItems.AddRange(render);
        MonitorCombo.ItemsSource = monitorItems;

        if (!string.IsNullOrWhiteSpace(AppServices.Settings.MicDeviceId))
        {
            MicCombo.SelectedValue = AppServices.Settings.MicDeviceId;
        }
        else
        {
            MicCombo.SelectedItem = capture.FirstOrDefault(d => !d.LooksLikeVirtualCable) ?? capture.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(AppServices.Settings.CableOutputDeviceId))
        {
            CableCombo.SelectedValue = AppServices.Settings.CableOutputDeviceId;
        }
        else
        {
            CableCombo.SelectedItem = render.FirstOrDefault(d =>
                d.LooksLikeVirtualCable && d.Name.Contains("Input", StringComparison.OrdinalIgnoreCase))
                ?? render.FirstOrDefault(d => d.LooksLikeVirtualCable)
                ?? render.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(AppServices.Settings.MonitorDeviceId))
        {
            MonitorCombo.SelectedValue = AppServices.Settings.MonitorDeviceId;
        }
        else
        {
            MonitorCombo.SelectedIndex = 0;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.MicDeviceId = MicCombo.SelectedValue as string;
        AppServices.Settings.CableOutputDeviceId = CableCombo.SelectedValue as string;
        var monitor = MonitorCombo.SelectedValue as string;
        AppServices.Settings.MonitorDeviceId = string.IsNullOrWhiteSpace(monitor) ? null : monitor;
        AppServices.Settings.PushToTalkIsMouse = _pendingPttIsMouse;
        AppServices.Settings.PushToTalkVirtualKey = _pendingPttCode is > 0 ? _pendingPttCode : null;
        AppServices.Settings.PushToTalkKeyName = AppServices.Settings.PushToTalkVirtualKey is null
            ? null
            : _pendingPttName;
        AppServices.Settings.FirstRunComplete = true;
        AppServices.Settings.Save();

        try
        {
            AppServices.Engine.Restart();
            Close();
        }
        catch (Exception ex)
        {
            CableStatus.Text = "Couldn't start audio: " + ex.Message;
        }
    }
}
