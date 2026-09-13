using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MicPipe.Data;
using MicPipe.Services;

namespace MicPipe.Views;

/// <summary>Quick F1–F12 assignment plus a read-out of every bind (any key/mouse binds come from the Clips window).</summary>
public sealed partial class HotkeysWindow : Window
{
    private const int FKeyCount = 12;
    private readonly ComboBox[] _fKeyBoxes = new ComboBox[FKeyCount];

    public HotkeysWindow()
    {
        InitializeComponent();
        WindowSetup.Apply(this, 504, 624, 420, 480);
        Refresh();
    }

    public void Refresh()
    {
        var clips = AppServices.Library.Clips;
        var clipNames = clips.ToDictionary(c => c.Id, c => c.Name);
        var options = new List<ClipOption> { new("", "— none —") };
        options.AddRange(clips.Select(c => new ClipOption(c.Id, c.Name)));

        var lines = AppServices.Settings.ClipKeybinds
            .OrderBy(b => b.IsMouse).ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase)
            .Select(b => $"{b.Label}  →  {(clipNames.TryGetValue(b.ClipId, out var n) ? n : "(missing clip)")}")
            .ToList();
        ActiveBindsText.Text = lines.Count == 0
            ? "None yet — bind keys in Clips, or assign F-keys below."
            : string.Join(Environment.NewLine, lines);

        var fKeyToClip = AppServices.Settings.ClipKeybinds
            .Where(IsManagedHere)
            .ToDictionary(b => ClipKeybind.FKeyNumber(b.Code)!.Value, b => b.ClipId);

        var textBrush = (Brush)((FrameworkElement)Content).Resources["TextBrush"];
        BindingsPanel.Children.Clear();
        for (var n = 1; n <= FKeyCount; n++)
        {
            var box = new ComboBox
            {
                ItemsSource = options,
                DisplayMemberPath = nameof(ClipOption.Name),
                SelectedValuePath = nameof(ClipOption.Id),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            box.SelectedValue = fKeyToClip.TryGetValue(n, out var clipId) && clipNames.ContainsKey(clipId) ? clipId : "";

            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = "F" + n, VerticalAlignment = VerticalAlignment.Center, Foreground = textBrush });
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            BindingsPanel.Children.Add(row);
            _fKeyBoxes[n - 1] = box;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in _fKeyBoxes)
        {
            box.SelectedIndex = 0;
        }
    }

    /// <summary>Only F1–F12 keyboard binds belong to this window; everything else (F13+, letters, mouse) is left alone.</summary>
    private static bool IsManagedHere(ClipKeybind b) => !b.IsMouse && ClipKeybind.FKeyNumber(b.Code) is >= 1 and <= FKeyCount;

    /// <summary>F1–F12 binds are replaced wholesale from the combo boxes.</summary>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var binds = AppServices.Settings.ClipKeybinds;
        binds.RemoveAll(IsManagedHere);
        for (var n = 1; n <= FKeyCount; n++)
        {
            if (_fKeyBoxes[n - 1].SelectedValue is string clipId && clipId.Length > 0)
            {
                binds.RemoveAll(b => b.ClipId == clipId);
                binds.Add(new ClipKeybind { ClipId = clipId, Code = ClipKeybind.FKeyCode(n), Label = "F" + n });
            }
        }

        AppServices.Settings.Save();
        AppServices.Hotkeys.ReloadBindings();
        Close();
    }

    private sealed record ClipOption(string Id, string Name);
}
