using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MicPipe.Services;

namespace MicPipe.Views;

public sealed partial class HotkeysWindow : Window
{
    private readonly Dictionary<string, ComboBox> _boxes = new();

    public HotkeysWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(420, 520));
        HoldRadio.IsChecked = AppServices.Settings.HotkeyHoldToPlay;
        ToggleRadio.IsChecked = !AppServices.Settings.HotkeyHoldToPlay;
        Refresh();
    }

    public void Refresh(string? focusClipId = null)
    {
        BindingsPanel.Children.Clear();
        _boxes.Clear();

        var none = new ClipOption { Id = "", Name = "— none —" };
        var options = new List<ClipOption> { none };
        options.AddRange(AppServices.Library.Clips.Select(c => new ClipOption { Id = c.Id, Name = c.Name }));

        for (var i = 1; i <= 12; i++)
        {
            var key = "F" + i;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = key,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextBrush"]
            };
            var box = new ComboBox
            {
                ItemsSource = options,
                DisplayMemberPath = "Name",
                SelectedValuePath = "Id",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (AppServices.Settings.HotkeyBindings.TryGetValue(key, out var clipId))
            {
                box.SelectedValue = clipId;
            }
            else
            {
                box.SelectedIndex = 0;
            }

            if (!string.IsNullOrEmpty(focusClipId) &&
                string.Equals(box.SelectedValue as string, focusClipId, StringComparison.Ordinal))
            {
                // already focused binding
            }

            Grid.SetColumn(box, 1);
            row.Children.Add(label);
            row.Children.Add(box);
            BindingsPanel.Children.Add(row);
            _boxes[key] = box;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in _boxes.Values)
        {
            box.SelectedIndex = 0;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.HotkeyBindings.Clear();
        foreach (var (key, box) in _boxes)
        {
            var id = box.SelectedValue as string;
            if (!string.IsNullOrWhiteSpace(id))
            {
                AppServices.Settings.HotkeyBindings[key] = id;
            }
        }

        AppServices.Settings.HotkeyHoldToPlay = HoldRadio.IsChecked == true;
        AppServices.Settings.Save();
        AppServices.Hotkeys.ReloadBindings();
        Close();
    }

    private sealed class ClipOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
