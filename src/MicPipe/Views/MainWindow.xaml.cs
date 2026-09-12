using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MicPipe.Services;
using Windows.UI;

namespace MicPipe.Views;

public sealed partial class MainWindow : Window
{
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(560, 280));
        ExtendsContentIntoTitleBar = true;

        MicVolumeSlider.Value = AppServices.Settings.MicVolume * 100;
        ClipVolumeSlider.Value = AppServices.Settings.ClipVolume * 100;
        MicVolumeLabel.Text = $"{(int)MicVolumeSlider.Value}%";
        ClipVolumeLabel.Text = $"{(int)ClipVolumeSlider.Value}%";
        _ready = true;

        AppServices.Engine.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateLive);
        AppServices.Engine.ClipChanged += (_, name) => DispatcherQueue.TryEnqueue(() =>
        {
            LastClipLabel.Text = string.IsNullOrEmpty(name) ? "Last: —" : $"Last: \"{name}\"";
        });
        UpdateLive();
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

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.Quit();
        }
    }
}
