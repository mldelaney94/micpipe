using Microsoft.UI.Xaml;
using MicPipe.Audio;
using MicPipe.Services;

namespace MicPipe.Views;

public sealed partial class DevicesWindow : Window
{
    public DevicesWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 440));
        Refresh();
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
            new() { Id = "", Name = "— none —", LooksLikeVirtualCable = false }
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
