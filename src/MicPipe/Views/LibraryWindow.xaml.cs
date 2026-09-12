using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MicPipe.Services;

namespace MicPipe.Views;

public sealed partial class LibraryWindow : Window
{
    public LibraryWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 420));
        Refresh();
    }

    public void Refresh()
    {
        var bindings = AppServices.Settings.HotkeyBindings
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Key)));

        ClipList.ItemsSource = AppServices.Library.Clips.Select(c => new ClipRow
        {
            Id = c.Id,
            Name = c.Name,
            DurationText = TimeSpan.FromSeconds(c.DurationSeconds).ToString(@"m\:ss"),
            HotkeyText = bindings.TryGetValue(c.Id, out var hk) ? hk : "—"
        }).ToList();
    }

    private ClipRow? Selected => ClipList.SelectedItem as ClipRow;

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row is null || !AppServices.Library.TryGet(row.Id, out var entry) || entry is null)
        {
            return;
        }

        AppServices.Engine.PlayClip(AppServices.Library.GetPath(entry), entry.Name);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => AppServices.Engine.StopClip();

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row is null) return;

        var box = new TextBox { Text = row.Name };
        var dialog = new ContentDialog
        {
            Title = "Rename clip",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            AppServices.Library.Rename(row.Id, box.Text.Trim());
            Refresh();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row is null) return;
        AppServices.Library.Delete(row.Id);
        Refresh();
    }

    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        WindowHub.OpenHotkeys(row?.Id);
    }

    private sealed class ClipRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string DurationText { get; set; } = "";
        public string HotkeyText { get; set; } = "";
    }
}
