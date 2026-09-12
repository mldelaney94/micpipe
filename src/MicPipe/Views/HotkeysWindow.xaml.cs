using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MicPipe.Data;
using MicPipe.Hotkeys;
using MicPipe.Services;

namespace MicPipe.Views;

public sealed partial class HotkeysWindow : Window
{
    private readonly Dictionary<string, ComboBox> _boxes = new();

    public HotkeysWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(504, 624));
        HoldRadio.IsChecked = AppServices.Settings.HotkeyHoldToPlay;
        ToggleRadio.IsChecked = !AppServices.Settings.HotkeyHoldToPlay;
        Refresh();
    }

    public void Refresh(string? focusClipId = null)
    {
        BindingsPanel.Children.Clear();
        _boxes.Clear();
        UpdateActiveBindsSummary();

        var none = new ClipOption { Id = "", Name = "— none —" };
        var options = new List<ClipOption> { none };
        options.AddRange(AppServices.Library.Clips.Select(c => new ClipOption { Id = c.Id, Name = c.Name }));

        var fKeyClipIds = ResolveFKeyClipMap();

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

            if (fKeyClipIds.TryGetValue(key, out var clipId) && !string.IsNullOrWhiteSpace(clipId))
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
                box.Focus(FocusState.Programmatic);
            }

            Grid.SetColumn(box, 1);
            row.Children.Add(label);
            row.Children.Add(box);
            BindingsPanel.Children.Add(row);
            _boxes[key] = box;
        }
    }

    private void UpdateActiveBindsSummary()
    {
        var lines = new List<string>();
        var clips = AppServices.Library.Clips.ToDictionary(c => c.Id, c => c.Name);

        foreach (var bind in AppServices.Settings.ClipKeybinds
                     .OrderBy(b => b.IsMouse)
                     .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase))
        {
            var clipName = clips.TryGetValue(bind.ClipId, out var name) ? name : "(missing clip)";
            var key = string.IsNullOrWhiteSpace(bind.Label)
                ? (bind.IsMouse ? "Mouse" + bind.Code : "VK " + bind.Code)
                : bind.Label;
            lines.Add($"{key}  →  {clipName}");
        }

        // Legacy dictionary entries not already represented in ClipKeybinds
        foreach (var pair in AppServices.Settings.HotkeyBindings
                     .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (AppServices.Settings.ClipKeybinds.Any(b =>
                    string.Equals(b.ClipId, pair.Value, StringComparison.Ordinal) &&
                    string.Equals(b.Label, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var clipName = clips.TryGetValue(pair.Value, out var name) ? name : "(missing clip)";
            lines.Add($"{pair.Key}  →  {clipName}");
        }

        ActiveBindsText.Text = lines.Count == 0
            ? "None yet — bind keys in Clips, or assign F-keys below."
            : string.Join(Environment.NewLine, lines);
    }

    /// <summary>F1–F12 → clip id from ClipKeybinds (Clips UI) and legacy HotkeyBindings.</summary>
    private static Dictionary<string, string> ResolveFKeyClipMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in AppServices.Settings.HotkeyBindings)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                map[pair.Key] = pair.Value;
            }
        }

        foreach (var bind in AppServices.Settings.ClipKeybinds)
        {
            if (bind.IsMouse || string.IsNullOrWhiteSpace(bind.ClipId))
            {
                continue;
            }

            string? fKey = null;
            if (!string.IsNullOrWhiteSpace(bind.Label) &&
                bind.Label.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(bind.Label.AsSpan(1), out var n) && n is >= 1 and <= 12)
            {
                fKey = "F" + n;
            }
            else if (bind.Code is >= 0x70 and <= 0x7B)
            {
                fKey = "F" + (bind.Code - 0x70 + 1);
            }

            if (fKey is not null)
            {
                map[fKey] = bind.ClipId;
            }
        }

        return map;
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
        // Replace F-key binds; keep non-F binds from Clips (e.g. Mouse4, letter keys)
        AppServices.Settings.ClipKeybinds.RemoveAll(IsFKeyBind);

        foreach (var (key, box) in _boxes)
        {
            var id = box.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            AppServices.Settings.HotkeyBindings[key] = id;
            if (HotkeyService.TryResolveVirtualKey(key, out var vk))
            {
                AppServices.Settings.ClipKeybinds.RemoveAll(b =>
                    b.ClipId == id || (!b.IsMouse && b.Code == (int)vk));
                AppServices.Settings.ClipKeybinds.Add(new ClipKeybind
                {
                    ClipId = id,
                    Code = (int)vk,
                    IsMouse = false,
                    Label = key
                });
            }
        }

        AppServices.Settings.HotkeyHoldToPlay = HoldRadio.IsChecked == true;
        AppServices.Settings.Save();
        AppServices.Hotkeys.ReloadBindings();
        Close();
    }

    private static bool IsFKeyBind(ClipKeybind b)
    {
        if (b.IsMouse)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(b.Label) &&
            b.Label.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            b.Label.Length <= 3 &&
            char.IsDigit(b.Label[^1]))
        {
            return true;
        }

        return b.Code is >= 0x70 and <= 0x7B;
    }

    private sealed class ClipOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }
}
