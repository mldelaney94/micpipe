using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MicPipe.Audio;
using MicPipe.Services;
using Windows.System;

namespace MicPipe.Views;

/// <summary>Volumes, live indicator, the PTT binding, and doors to the tool windows.</summary>
public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _progressTimer;
    private bool _ready;
    private bool _capturingPtt;

    public MainWindow()
    {
        InitializeComponent();
        WindowSetup.Apply(this, 672, 504, 576, 432);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        UpdateCaptionSpacer();
        AppWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdateCaptionSpacer);

        MicVolumeSlider.Value = AppServices.Settings.MicVolume * 100;
        ClipVolumeSlider.Value = AppServices.Settings.ClipVolume * 100;
        MicVolumeLabel.Text = $"{(int)MicVolumeSlider.Value}%";
        ClipVolumeLabel.Text = $"{(int)ClipVolumeSlider.Value}%";
        _ready = true;

        Content.KeyDown += Content_KeyDown;
        Content.PointerPressed += Content_PointerPressed;
        UpdatePttButton();

        _progressTimer = DispatcherQueue.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(33);
        _progressTimer.Tick += (_, _) =>
        {
            if (AppServices.Engine.GetClipProgress() is double progress)
            {
                PlayProgress.Value = progress;
            }
            else
            {
                ShowClip(null);
            }
        };

        AppServices.Engine.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateLive);
        AppServices.Engine.ClipChanged += (_, clip) => DispatcherQueue.TryEnqueue(() => ShowClip(clip));
        UpdateLive();
    }

    private void ShowClip(ActiveClip? clip)
    {
        LastClipLabel.Text = clip is null ? "Last: —" : $"Last: \"{clip.Name}\"";
        PlayProgress.Value = 0;
        PlayProgress.Visibility = clip is null ? Visibility.Collapsed : Visibility.Visible;
        if (clip is null)
        {
            _progressTimer.Stop();
        }
        else
        {
            _progressTimer.Start();
        }
    }

    private void UpdateLive()
    {
        var live = AppServices.Engine.IsLive;
        LiveLabel.Text = live ? "Live" : "Idle";
        LiveDot.Fill = (Brush)((FrameworkElement)Content).Resources[live ? "LiveBrush" : "MutedTextBrush"];
    }

    /// <summary>Keeps the custom title bar clear of the system caption buttons at any DPI.</summary>
    private void UpdateCaptionSpacer()
    {
        double width = 140;
        try
        {
            var scale = Content?.XamlRoot?.RasterizationScale ?? 1.0;
            width = Math.Max(width, AppWindow.TitleBar.RightInset / Math.Max(scale, 0.5) + 8);
        }
        catch
        {
            // TitleBar insets are unavailable on some presenters; the default width is safe.
        }

        CaptionSpacer.Width = new GridLength(width);
    }

    // --- Push-to-talk binding -------------------------------------------------

    private void UpdatePttButton()
    {
        var name = AppServices.Settings.PushToTalkKeyName;
        PttButton.Content = string.IsNullOrWhiteSpace(name) ? "PTT: none" : "PTT: " + name;
    }

    private void Ptt_Click(object sender, RoutedEventArgs e)
    {
        _capturingPtt = true;
        PttHint.Visibility = Visibility.Visible;
        PttButton.Content = "PTT: press…";
    }

    private void Content_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturingPtt)
        {
            return;
        }

        e.Handled = true;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                EndPttCapture();
                break;
            case VirtualKey.Back or VirtualKey.Delete:
                SavePtt(null, false, null);
                break;
            case VirtualKey.Space or VirtualKey.Enter:
                break; // the focused PTT button consumes these as clicks
            default:
                if (!InputCapture.IsModifier(e.Key))
                {
                    SavePtt((int)e.Key, false, InputCapture.KeyLabel(e.Key));
                }

                break;
        }
    }

    private void Content_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_capturingPtt && InputCapture.BindableMouseButton(e, Content) is int button)
        {
            e.Handled = true;
            SavePtt(button, true, "Mouse" + button);
        }
    }

    private void SavePtt(int? code, bool isMouse, string? name)
    {
        var settings = AppServices.Settings;
        settings.PushToTalkVirtualKey = code;
        settings.PushToTalkIsMouse = isMouse;
        settings.PushToTalkKeyName = name;
        settings.Save();
        EndPttCapture();
    }

    private void EndPttCapture()
    {
        _capturingPtt = false;
        PttHint.Visibility = Visibility.Collapsed;
        UpdatePttButton();
    }

    // --- Volumes and navigation ------------------------------------------------

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
