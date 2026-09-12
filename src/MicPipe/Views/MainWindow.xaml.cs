using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MicPipe.Services;
using Windows.System;
using Windows.UI;

namespace MicPipe.Views;

public sealed partial class MainWindow : Window
{
    private bool _ready;
    private bool _capturingPtt;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _playTimer;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(672, 504));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 576;
            presenter.PreferredMinimumHeight = 432;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        UpdateCaptionSpacer();
        AppWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdateCaptionSpacer);
        TrySetWindowIcon();

        MicVolumeSlider.Value = AppServices.Settings.MicVolume * 100;
        ClipVolumeSlider.Value = AppServices.Settings.ClipVolume * 100;
        MicVolumeLabel.Text = $"{(int)MicVolumeSlider.Value}%";
        ClipVolumeLabel.Text = $"{(int)ClipVolumeSlider.Value}%";
        _ready = true;

        Content.KeyDown += Content_KeyDown;
        Content.PointerPressed += Content_PointerPressed;
        Activated += (_, _) => DispatcherQueue.TryEnqueue(UpdatePttButton);
        UpdatePttButton();

        _playTimer = DispatcherQueue.CreateTimer();
        _playTimer.Interval = TimeSpan.FromMilliseconds(33);
        _playTimer.Tick += (_, _) =>
        {
            if (!AppServices.Engine.IsClipPlaying)
            {
                PlayProgress.Visibility = Visibility.Collapsed;
                PlayProgress.Value = 0;
                _playTimer?.Stop();
                return;
            }

            PlayProgress.Visibility = Visibility.Visible;
            PlayProgress.Value = AppServices.Engine.GetClipProgress() ?? 0;
        };

        AppServices.Engine.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateLive);
        AppServices.Engine.ClipChanged += (_, name) => DispatcherQueue.TryEnqueue(() =>
        {
            LastClipLabel.Text = string.IsNullOrEmpty(name) ? "Last: —" : $"Last: \"{name}\"";
            if (string.IsNullOrEmpty(name))
            {
                PlayProgress.Visibility = Visibility.Collapsed;
                PlayProgress.Value = 0;
                _playTimer?.Stop();
            }
            else
            {
                PlayProgress.Value = 0;
                PlayProgress.Visibility = Visibility.Visible;
                _playTimer?.Start();
            }
        });
        UpdateLive();
    }

    private void UpdatePttButton()
    {
        var name = AppServices.Settings.PushToTalkKeyName;
        PttButton.Content = string.IsNullOrWhiteSpace(name)
            ? "PTT: none"
            : "PTT: " + name;
    }

    private void Ptt_Click(object sender, RoutedEventArgs e)
    {
        _capturingPtt = true;
        PttHint.Visibility = Visibility.Visible;
        PttHint.Text = "Press a key or mouse button (e.g. Mouse4). Esc cancels…";
        PttButton.Content = "PTT: press…";
        Content.Focus(FocusState.Programmatic);
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
            UpdatePttButton();
            return;
        }

        BindPtt(isMouse: false, code: (int)e.Key, name: e.Key.ToString());
    }

    private void Content_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_capturingPtt)
        {
            return;
        }

        var point = e.GetCurrentPoint(Content);
        var kind = point.Properties.PointerUpdateKind;
        int? button = kind switch
        {
            PointerUpdateKind.XButton1Pressed => 4,
            PointerUpdateKind.XButton2Pressed => 5,
            PointerUpdateKind.MiddleButtonPressed => 3,
            _ => null
        };

        if (button is null)
        {
            // Ignore left/right so UI clicks don't steal the bind mid-capture
            return;
        }

        e.Handled = true;
        BindPtt(isMouse: true, code: button.Value, name: "Mouse" + button.Value);
    }

    private void BindPtt(bool isMouse, int code, string name)
    {
        _capturingPtt = false;
        PttHint.Visibility = Visibility.Collapsed;
        AppServices.Settings.PushToTalkIsMouse = isMouse;
        AppServices.Settings.PushToTalkVirtualKey = code;
        AppServices.Settings.PushToTalkKeyName = name;
        AppServices.Settings.Save();
        UpdatePttButton();
    }

    private void UpdateCaptionSpacer()
    {
        try
        {
            var scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
            var caps = AppWindow.TitleBar.RightInset;
            var dip = scale > 0 ? caps / scale : caps;
            CaptionSpacer.Width = new GridLength(Math.Max(140, dip + 8));
        }
        catch
        {
            CaptionSpacer.Width = new GridLength(140);
        }
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MicPipe.ico");
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

    private void UpdateLive()
    {
        var live = AppServices.Engine.IsLive;
        LiveLabel.Text = live ? "Live" : "Idle";
        LiveDot.Fill = new SolidColorBrush(live
            ? Color.FromArgb(255, 0x3D, 0xDC, 0x84)
            : Color.FromArgb(255, 0x9A, 0xA0, 0xA6));
    }

    private void MicVolume_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        MicVolumeLabel.Text = $"{(int)e.NewValue}%";
        AppServices.Engine.SetMicVolume((float)(e.NewValue / 100.0));
    }

    private void ClipVolume_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        ClipVolumeLabel.Text = $"{(int)e.NewValue}%";
        AppServices.Engine.SetClipVolume((float)(e.NewValue / 100.0));
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => AppServices.Engine.StopClip();

    private void Import_Click(object sender, RoutedEventArgs e) => WindowHub.OpenImport();

    private void Hotkeys_Click(object sender, RoutedEventArgs e) => WindowHub.OpenHotkeys();

    private void Devices_Click(object sender, RoutedEventArgs e) => WindowHub.OpenDevices();

    private void Clips_Click(object sender, RoutedEventArgs e) => WindowHub.OpenLibrary();
}
