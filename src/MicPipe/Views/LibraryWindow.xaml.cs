using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MicPipe.Audio;
using MicPipe.Services;
using Windows.System;

namespace MicPipe.Views;

public sealed partial class LibraryWindow : Window
{
    private readonly List<ClipRow> _rows = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _playTimer;
    private readonly EventHandler<ActiveClip?> _onClipChanged;
    private ClipRow? _playingRow;

    // Set while the "Bind hotkey" dialog is open; mouse binds arrive on the window, not the dialog.
    private ContentDialog? _bindDialog;
    private ClipRow? _bindingRow;

    // Dialogs render in the XamlRoot's popup root, outside this window's resource scope. A dictionary can only be
    // owned by one element, so each dialog loads its own copy of this window's stylesheet.
    private static ResourceDictionary WindowPalette =>
        new() { Source = new Uri("ms-appx:///Views/LibraryWindow.Styles.xaml") };

    public LibraryWindow()
    {
        InitializeComponent();
        WindowSetup.Apply(this, 768, 672, 624, 504);
        Content.PointerPressed += Content_PointerPressed;

        _playTimer = DispatcherQueue.CreateTimer();
        _playTimer.Interval = TimeSpan.FromMilliseconds(33);
        _playTimer.Tick += (_, _) =>
        {
            if (_playingRow is null || AppServices.Engine.GetClipProgress() is not double progress)
            {
                ShowPlaying(null);
            }
            else
            {
                _playingRow.Progress = progress;
            }
        };

        _onClipChanged = (_, clip) => DispatcherQueue.TryEnqueue(() =>
            ShowPlaying(clip is null ? null : _rows.FirstOrDefault(r => r.Path == clip.Path)));
        AppServices.Engine.ClipChanged += _onClipChanged;
        Closed += (_, _) =>
        {
            _playTimer.Stop();
            AppServices.Engine.ClipChanged -= _onClipChanged;
        };

        Refresh();
    }

    public void Refresh()
    {
        var labels = AppServices.Settings.ClipKeybinds
            .GroupBy(b => b.ClipId)
            .ToDictionary(g => g.Key, g => g.First().Label);

        _rows.Clear();
        foreach (var clip in AppServices.Library.Clips)
        {
            _rows.Add(new ClipRow
            {
                Id = clip.Id,
                Name = clip.Name,
                Path = AppServices.Library.GetPath(clip),
                DurationText = TimeSpan.FromSeconds(clip.DurationSeconds).ToString(@"m\:ss"),
                HotkeyLabel = labels.GetValueOrDefault(clip.Id)
            });
        }

        ClipList.ItemsSource = null;
        ClipList.ItemsSource = _rows;
        ShowPlaying(AppServices.Engine.Current is ActiveClip current ? _rows.FirstOrDefault(r => r.Path == current.Path) : null);
    }

    private ClipRow? Selected => ClipList.SelectedItem as ClipRow;

    private void ShowPlaying(ClipRow? row)
    {
        foreach (var r in _rows)
        {
            r.IsPlaying = false;
            r.Progress = 0;
        }

        _playingRow = row;
        if (row is null)
        {
            _playTimer.Stop();
        }
        else
        {
            row.IsPlaying = true;
            _playTimer.Start();
        }
    }

    // --- Hotkey binding ---------------------------------------------------------

    private async void BindRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not ClipRow row || _bindDialog is not null)
        {
            return;
        }

        ClipList.SelectedItem = row;
        _bindingRow = row;

        var capture = new Button { Content = "Waiting for key…", HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Press any key (F1–F12, letters, or Mouse4/5). Esc cancels.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(capture);

        _bindDialog = new ContentDialog
        {
            Title = "Bind hotkey",
            Content = panel,
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot,
            Resources = WindowPalette
        };

        // handledEventsToo: the Button marks Space/Enter handled before the dialog would see them.
        _bindDialog.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(BindDialog_KeyDown), handledEventsToo: true);
        _bindDialog.Opened += (_, _) => capture.Focus(FocusState.Programmatic);
        _bindDialog.Closed += (_, _) =>
        {
            _bindDialog = null;
            _bindingRow = null;
        };

        await _bindDialog.ShowAsync();
    }

    private void BindDialog_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            _bindDialog?.Hide();
        }
        else if (!InputCapture.IsModifier(e.Key))
        {
            CompleteBind((int)e.Key, isMouse: false, InputCapture.KeyLabel(e.Key));
        }
    }

    private void Content_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_bindingRow is not null && InputCapture.BindableMouseButton(e, Content) is int button)
        {
            e.Handled = true;
            CompleteBind(button, isMouse: true, "Mouse" + button);
        }
    }

    private void CompleteBind(int code, bool isMouse, string label)
    {
        if (_bindingRow is not ClipRow row)
        {
            return;
        }

        _bindingRow = null;
        AppServices.Hotkeys.BindClip(row.Id, code, isMouse, label);
        // One key per clip: drop the label from whichever row previously held this key.
        foreach (var other in _rows.Where(r => r != row && r.HotkeyLabel == label))
        {
            other.HotkeyLabel = null;
        }

        row.HotkeyLabel = label;
        WindowHub.RefreshHotkeysIfOpen();
        _bindDialog?.Hide();
    }

    private void ClearBind(ClipRow row)
    {
        AppServices.Hotkeys.ClearClipBind(row.Id);
        row.HotkeyLabel = null;
        WindowHub.RefreshHotkeysIfOpen();
    }

    // --- Row actions -------------------------------------------------------------

    private void ClipList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (((e.OriginalSource as FrameworkElement)?.DataContext as ClipRow ?? Selected) is not ClipRow row)
        {
            return;
        }

        ClipList.SelectedItem = row;
        var flyout = new MenuFlyout();
        flyout.Items.Add(MenuItem("Rename", async () => await RenameAsync(row)));
        flyout.Items.Add(MenuItem("Edit in trimmer", () => EditClip(row)));
        flyout.Items.Add(MenuItem("Clear hotkey", () => ClearBind(row)));
        flyout.Items.Add(MenuItem("Delete", () => DeleteClip(row)));
        flyout.ShowAt(ClipList, e.GetPosition(ClipList));
    }

    private static MenuFlyoutItem MenuItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is ClipRow row)
        {
            // Decoding happens inside PlayClip; keep it off the UI thread.
            await Task.Run(() => AppServices.Engine.PlayClip(row.Path, row.Name));
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => AppServices.Engine.StopClip();

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is ClipRow row) await RenameAsync(row);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is ClipRow row) EditClip(row);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is ClipRow row) DeleteClip(row);
    }

    private async Task RenameAsync(ClipRow row)
    {
        var box = new TextBox { Text = row.Name };
        var dialog = new ContentDialog
        {
            Title = "Rename clip",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            Resources = WindowPalette
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && box.Text.Trim() is { Length: > 0 } name)
        {
            AppServices.Library.Rename(row.Id, name);
            Refresh();
        }
    }

    private static void EditClip(ClipRow row) =>
        WindowHub.OpenTrim(new TrimRequest(row.Path, row.Name, EditClipId: row.Id));

    private void DeleteClip(ClipRow row)
    {
        AppServices.Library.Delete(row.Id);
        AppServices.Hotkeys.ClearClipBind(row.Id);
        Refresh();
        WindowHub.RefreshHotkeysIfOpen();
    }

    private sealed class ClipRow : INotifyPropertyChanged
    {
        private double _progress;
        private bool _isPlaying;
        private string? _hotkeyLabel;

        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Path { get; init; }
        public required string DurationText { get; init; }

        public string? HotkeyLabel
        {
            get => _hotkeyLabel;
            set
            {
                _hotkeyLabel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BindButtonText));
            }
        }

        public string BindButtonText => string.IsNullOrWhiteSpace(HotkeyLabel) ? "Bind" : HotkeyLabel;

        public double Progress
        {
            get => _progress;
            set
            {
                if (Math.Abs(_progress - value) < 0.001) return;
                _progress = value;
                OnPropertyChanged();
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying == value) return;
                _isPlaying = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressVisibility));
            }
        }

        public Visibility ProgressVisibility => IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
