using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MicPipe.Services;
using Windows.System;

namespace MicPipe.Views;

public sealed partial class LibraryWindow : Window
{
    private readonly List<ClipRow> _rows = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _playTimer;
    private string? _playingClipId;
    private string? _bindingClipId;
    private ContentDialog? _bindDialog;
    private bool _bindCompleted;

    public LibraryWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(768, 672));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 624;
            presenter.PreferredMinimumHeight = 504;
        }

        Content.PointerPressed += Content_PointerPressed;

        _playTimer = DispatcherQueue.CreateTimer();
        _playTimer.Interval = TimeSpan.FromMilliseconds(33);
        _playTimer.Tick += (_, _) => UpdatePlayProgress();

        AppServices.Engine.ClipChanged += (_, name) => DispatcherQueue.TryEnqueue(() => OnClipChanged(name));

        Refresh();
    }

    public void Refresh()
    {
        var binds = AppServices.Settings.ClipKeybinds
            .GroupBy(b => b.ClipId)
            .ToDictionary(g => g.Key, g => g.First().Label);

        foreach (var kv in AppServices.Settings.HotkeyBindings)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value) && !binds.ContainsKey(kv.Value))
            {
                binds[kv.Value] = kv.Key;
            }
        }

        _rows.Clear();
        foreach (var c in AppServices.Library.Clips)
        {
            _rows.Add(new ClipRow
            {
                Id = c.Id,
                Name = c.Name,
                DurationText = TimeSpan.FromSeconds(c.DurationSeconds).ToString(@"m\:ss"),
                HotkeyLabel = binds.TryGetValue(c.Id, out var hk) ? hk : null
            });
        }

        ClipList.ItemsSource = null;
        ClipList.ItemsSource = _rows;
    }

    private ClipRow? Selected => ClipList.SelectedItem as ClipRow;

    private void OnClipChanged(string? name)
    {
        foreach (var row in _rows)
        {
            row.Progress = 0;
            row.IsPlaying = false;
        }

        if (string.IsNullOrEmpty(name))
        {
            _playingClipId = null;
            _playTimer?.Stop();
            return;
        }

        var match = _rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        _playingClipId = match?.Id;
        if (match is not null)
        {
            match.IsPlaying = true;
            _playTimer?.Start();
        }
    }

    private void UpdatePlayProgress()
    {
        if (!AppServices.Engine.IsClipPlaying || _playingClipId is null)
        {
            foreach (var r in _rows)
            {
                r.IsPlaying = false;
                r.Progress = 0;
            }

            _playTimer?.Stop();
            return;
        }

        var progress = AppServices.Engine.GetClipProgress() ?? 0;
        var row = _rows.FirstOrDefault(r => r.Id == _playingClipId);
        if (row is not null)
        {
            row.IsPlaying = true;
            row.Progress = progress;
        }
    }

    private async void BindRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button || (sender as Button)?.Tag is not ClipRow row)
        {
            return;
        }

        ClipList.SelectedItem = row;
        _bindingClipId = row.Id;
        _bindCompleted = false;

        var hint = new TextBlock
        {
            Text = "Select any key (F1–F12, letters, or Mouse4/5).\nEsc cancels.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // Focusable surface that actually receives F-keys
        var capture = new Button
        {
            Content = "Waiting for key…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["ToolButtonStyle"]
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(hint);
        panel.Children.Add(capture);

        _bindDialog = new ContentDialog
        {
            Title = "Bind hotkey",
            Content = panel,
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };

        void OnKey(object s, KeyRoutedEventArgs args)
        {
            if (_bindingClipId is null)
            {
                return;
            }

            args.Handled = true;
            if (args.Key is VirtualKey.Escape)
            {
                CancelBind();
                return;
            }

            // Skip modifier-only presses
            if (args.Key is VirtualKey.Shift or VirtualKey.Control or VirtualKey.Menu or VirtualKey.LeftWindows
                or VirtualKey.RightWindows or VirtualKey.LeftShift or VirtualKey.RightShift
                or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftMenu or VirtualKey.RightMenu)
            {
                return;
            }

            CompleteBind(isMouse: false, code: (int)args.Key, label: FormatKeyLabel(args.Key));
        }

        capture.KeyDown += OnKey;
        capture.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKey), handledEventsToo: true);
        panel.KeyDown += OnKey;
        _bindDialog.KeyDown += OnKey;

        _bindDialog.Opened += async (_, _) =>
        {
            await Task.Delay(50);
            capture.Focus(FocusState.Programmatic);
        };

        _bindDialog.Closed += (_, _) =>
        {
            if (!_bindCompleted)
            {
                _bindingClipId = null;
            }

            _bindDialog = null;
        };

        _ = await _bindDialog.ShowAsync();
    }

    private static string FormatKeyLabel(VirtualKey key)
    {
        var name = key.ToString();
        if (name.StartsWith("Number", StringComparison.Ordinal) && name.Length == 7)
        {
            return name[^1].ToString();
        }

        return name;
    }

    private void CancelBind()
    {
        _bindingClipId = null;
        _bindCompleted = false;
        _bindDialog?.Hide();
    }

    private void CompleteBind(bool isMouse, int code, string label)
    {
        if (_bindingClipId is null || _bindCompleted)
        {
            return;
        }

        var clipId = _bindingClipId;
        var row = _rows.FirstOrDefault(r => r.Id == clipId);

        _bindCompleted = true;
        _bindingClipId = null;

        AppServices.Hotkeys.BindClip(clipId, code, isMouse, label);

        if (row is not null)
        {
            row.HotkeyLabel = label;
        }
        else
        {
            Refresh();
        }

        WindowHub.RefreshHotkeysIfOpen();
        _bindDialog?.Hide();
    }

    private void Content_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_bindingClipId is null || _bindDialog is null)
        {
            return;
        }

        var kind = e.GetCurrentPoint(Content).Properties.PointerUpdateKind;
        int? button = kind switch
        {
            PointerUpdateKind.XButton1Pressed => 4,
            PointerUpdateKind.XButton2Pressed => 5,
            PointerUpdateKind.MiddleButtonPressed => 3,
            _ => null
        };

        if (button is null)
        {
            return;
        }

        e.Handled = true;
        CompleteBind(isMouse: true, code: button.Value, label: "Mouse" + button.Value);
    }

    private void ClipList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var row = (e.OriginalSource as FrameworkElement)?.DataContext as ClipRow ?? Selected;
        if (row is null && e.OriginalSource is FrameworkElement fe)
        {
            var item = fe;
            while (item is not null && item is not ListViewItem)
            {
                item = item.Parent as FrameworkElement;
            }

            if (item is ListViewItem lvi)
            {
                row = lvi.Content as ClipRow;
            }
        }

        if (row is null)
        {
            return;
        }

        ClipList.SelectedItem = row;
        var flyout = new MenuFlyout();
        var rename = new MenuFlyoutItem { Text = "Rename" };
        rename.Click += async (_, _) => await RenameAsync(row);
        var edit = new MenuFlyoutItem { Text = "Edit in scrubber" };
        edit.Click += (_, _) => EditClip(row);
        var clearBind = new MenuFlyoutItem { Text = "Clear hotkey" };
        clearBind.Click += (_, _) =>
        {
            AppServices.Hotkeys.ClearClipBind(row.Id);
            row.HotkeyLabel = null;
            WindowHub.RefreshHotkeysIfOpen();
        };
        var del = new MenuFlyoutItem { Text = "Delete" };
        del.Click += (_, _) =>
        {
            AppServices.Library.Delete(row.Id);
            AppServices.Hotkeys.ClearClipBind(row.Id);
            Refresh();
            WindowHub.RefreshHotkeysIfOpen();
        };
        flyout.Items.Add(rename);
        flyout.Items.Add(edit);
        flyout.Items.Add(clearBind);
        flyout.Items.Add(del);
        flyout.ShowAt(ClipList, e.GetPosition(ClipList));
    }

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
        if (Selected is ClipRow row)
        {
            await RenameAsync(row);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is ClipRow row)
        {
            EditClip(row);
        }
    }

    private async Task RenameAsync(ClipRow row)
    {
        var box = new TextBox { Text = row.Name, AcceptsReturn = false };
        var dialog = new ContentDialog
        {
            Title = "Rename clip",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var savedWithEnter = false;
        box.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            savedWithEnter = true;
            dialog.Hide();
        };

        var result = await dialog.ShowAsync();
        if (savedWithEnter || result == ContentDialogResult.Primary)
        {
            AppServices.Library.Rename(row.Id, box.Text.Trim());
            Refresh();
        }
    }

    private void EditClip(ClipRow row)
    {
        if (!AppServices.Library.TryGet(row.Id, out var entry) || entry is null)
        {
            return;
        }

        WindowHub.OpenTrim(AppServices.Library.GetPath(entry), entry.Name, entry.Id);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row is null) return;
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

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string DurationText { get; set; } = "";

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

        public string BindButtonText =>
            string.IsNullOrWhiteSpace(HotkeyLabel) ? "Bind" : HotkeyLabel!;

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

        public Visibility ProgressVisibility =>
            IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
