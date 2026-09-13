using Microsoft.UI.Xaml;
using MicPipe.Audio;
using MicPipe.Services;

namespace MicPipe.Views;

public sealed partial class DevicesWindow : Window
{
    private static readonly AudioDeviceInfo DefaultSpeakers = new("", "— default speakers —", false);

    public DevicesWindow()
    {
        InitializeComponent();
        WindowSetup.Apply(this, 672, 640, 576, 480);
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var settings = AppServices.Settings;
        var capture = AudioEngine.ListCaptureDevices();
        var render = AudioEngine.ListRenderDevices();

        CableStatus.Text = AudioEngine.HasVirtualCableInstalled()
            ? "Virtual cable detected."
            : "Virtual cable not detected. Install VB-Audio Virtual Cable, then Refresh.";

        MicCombo.ItemsSource = capture;
        CableCombo.ItemsSource = render;
        MonitorCombo.ItemsSource = render.Prepend(DefaultSpeakers).ToList();

        // Saved device if still present, else a sensible guess.
        MicCombo.SelectedItem =
            capture.FirstOrDefault(d => d.Id == settings.MicDeviceId) ??
            capture.FirstOrDefault(d => !d.LooksLikeVirtualCable) ??
            capture.FirstOrDefault();
        CableCombo.SelectedItem =
            render.FirstOrDefault(d => d.Id == settings.CableOutputDeviceId) ??
            render.FirstOrDefault(d => d.LooksLikeVirtualCable && d.Name.Contains("Input", StringComparison.OrdinalIgnoreCase)) ??
            render.FirstOrDefault(d => d.LooksLikeVirtualCable) ??
            render.FirstOrDefault();
        MonitorCombo.SelectedItem =
            render.FirstOrDefault(d => d.Id == settings.MonitorDeviceId) ?? DefaultSpeakers;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppServices.Settings;
        settings.MicDeviceId = MicCombo.SelectedValue as string;
        settings.CableOutputDeviceId = CableCombo.SelectedValue as string;
        settings.MonitorDeviceId = MonitorCombo.SelectedValue is string monitor && monitor.Length > 0 ? monitor : null;
        settings.Save();

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
