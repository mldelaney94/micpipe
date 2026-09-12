using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MicPipe.Import;
using MicPipe.Services;
using Windows.UI;

namespace MicPipe.Views;

public sealed partial class TrimWindow : Window
{
    private string? _sourcePath;
    private string? _editClipId;
    private TimeSpan _duration;
    private float[] _peaks = Array.Empty<float>();
    private bool _ready;
    private bool _draggingStart;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _playTimer;
    private double? _playhead; // 0–1 within selection while previewing

    public TrimWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1080, 816));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 768;
            presenter.PreferredMinimumHeight = 624;
        }

        WaveCanvas.SizeChanged += (_, _) => DrawWaveform();
        TrySetWindowIcon();

        _playTimer = DispatcherQueue.CreateTimer();
        _playTimer.Interval = TimeSpan.FromMilliseconds(16);
        _playTimer.Tick += (_, _) =>
        {
            if (!AppServices.Engine.IsClipPlaying)
            {
                StopPlayhead();
                return;
            }

            _playhead = AppServices.Engine.GetClipProgress() ?? 0;
            DrawWaveform();
        };

        AppServices.Engine.ClipChanged += (_, name) => DispatcherQueue.TryEnqueue(() =>
        {
            if (string.IsNullOrEmpty(name))
            {
                StopPlayhead();
            }
            else
            {
                _playhead = 0;
                _playTimer?.Start();
                DrawWaveform();
            }
        });

        Closed += (_, _) =>
        {
            _playTimer?.Stop();
            _playTimer = null;
        };
    }

    private void StopPlayhead()
    {
        _playTimer?.Stop();
        _playhead = null;
        DrawWaveform();
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MicPipe.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // non-fatal
        }
    }

    public void LoadSource(string path, string? suggestedName, string? editClipId = null)
    {
        StopPlayhead();
        _sourcePath = path;
        _editClipId = editClipId;
        SourceLabel.Text = string.IsNullOrEmpty(editClipId)
            ? "source: " + path
            : "editing: " + (suggestedName ?? path);
        SaveButton.Content = string.IsNullOrEmpty(editClipId) ? "Save to library" : "Save changes";
        NameBox.Text = suggestedName ?? System.IO.Path.GetFileNameWithoutExtension(path);
        _duration = WaveformBuilder.GetDuration(path);
        _peaks = WaveformBuilder.BuildPeaks(path);
        EndLabel.Text = Format(_duration);
        _ready = true;
        StartSlider.Value = 0;
        EndSlider.Value = 1000;
        UpdateLabels();
        DrawWaveform();
    }

    private void Range_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        if (EndSlider.Value <= StartSlider.Value)
        {
            if (ReferenceEquals(sender, StartSlider))
            {
                EndSlider.Value = Math.Min(1000, StartSlider.Value + 1);
            }
            else
            {
                StartSlider.Value = Math.Max(0, EndSlider.Value - 1);
            }
        }

        UpdateLabels();
        DrawWaveform();
    }

    private void UpdateLabels()
    {
        var start = RatioToTime(StartSlider.Value / 1000.0);
        var end = RatioToTime(EndSlider.Value / 1000.0);
        StartLabel.Text = Format(start);
        EndLabel.Text = Format(_duration);
        SelectionLabel.Text = "selection " + Format(end - start);
    }

    private TimeSpan RatioToTime(double ratio) =>
        TimeSpan.FromTicks((long)(_duration.Ticks * Math.Clamp(ratio, 0, 1)));

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";

    private void DrawWaveform()
    {
        WaveCanvas.Children.Clear();
        if (_peaks.Length == 0 || WaveCanvas.ActualWidth <= 0)
        {
            return;
        }

        var w = WaveCanvas.ActualWidth;
        var h = WaveCanvas.ActualHeight;
        var mid = h / 2;
        var barWidth = Math.Max(1, w / _peaks.Length);

        var startX = w * (StartSlider.Value / 1000.0);
        var endX = w * (EndSlider.Value / 1000.0);

        // Selection band
        WaveCanvas.Children.Add(new Rectangle
        {
            Width = Math.Max(1, endX - startX),
            Height = h,
            Fill = new SolidColorBrush(Color.FromArgb(40, 0xE0, 0xA0, 0x45))
        });
        Canvas.SetLeft(WaveCanvas.Children[^1], startX);

        // Scrubbed / played region within selection
        if (_playhead is double progress)
        {
            var playX = startX + (endX - startX) * Math.Clamp(progress, 0, 1);
            WaveCanvas.Children.Add(new Rectangle
            {
                Width = Math.Max(1, playX - startX),
                Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(120, 0xE0, 0xA0, 0x45))
            });
            Canvas.SetLeft(WaveCanvas.Children[^1], startX);

            var playhead = new Rectangle
            {
                Width = 2,
                Height = h,
                Fill = new SolidColorBrush(Color.FromArgb(255, 0xE0, 0xA0, 0x45))
            };
            Canvas.SetLeft(playhead, playX - 1);
            WaveCanvas.Children.Add(playhead);
        }

        for (var i = 0; i < _peaks.Length; i++)
        {
            var amp = _peaks[i] * (h * 0.45);
            var x = i * barWidth;
            var inPlayed = _playhead is double p &&
                           x >= startX &&
                           x <= startX + (endX - startX) * Math.Clamp(p, 0, 1);

            var rect = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(1, amp * 2),
                Fill = new SolidColorBrush(inPlayed
                    ? Color.FromArgb(255, 0xE0, 0xA0, 0x45)
                    : Color.FromArgb(255, 0xE8, 0xEA, 0xED))
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, mid - amp);
            WaveCanvas.Children.Add(rect);
        }
    }

    private void Wave_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var x = e.GetCurrentPoint(WaveCanvas).Position.X;
        var ratio = x / Math.Max(1, WaveCanvas.ActualWidth) * 1000;
        var distStart = Math.Abs(ratio - StartSlider.Value);
        var distEnd = Math.Abs(ratio - EndSlider.Value);
        _draggingStart = distStart <= distEnd;
        if (_draggingStart) StartSlider.Value = ratio;
        else EndSlider.Value = ratio;
    }

    private void Wave_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!e.Pointer.IsInContact) return;
        var x = e.GetCurrentPoint(WaveCanvas).Position.X;
        var ratio = x / Math.Max(1, WaveCanvas.ActualWidth) * 1000;
        if (_draggingStart) StartSlider.Value = ratio;
        else EndSlider.Value = ratio;
    }

    private void Wave_PointerReleased(object sender, PointerRoutedEventArgs e) { }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_sourcePath is null) return;
        try
        {
            var start = RatioToTime(StartSlider.Value / 1000.0);
            var end = RatioToTime(EndSlider.Value / 1000.0);
            var trimmed = await AppServices.Transcoder.TrimToWavAsync(_sourcePath, start, end);
            _playhead = 0;
            DrawWaveform();
            _playTimer?.Start();
            AppServices.Engine.PreviewClipLocal(trimmed, NameBox.Text);
        }
        catch (Exception ex)
        {
            StopPlayhead();
            SourceLabel.Text = ex.Message;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_sourcePath is null) return;
        try
        {
            var start = RatioToTime(StartSlider.Value / 1000.0);
            var end = RatioToTime(EndSlider.Value / 1000.0);
            var trimmed = await AppServices.Transcoder.TrimToWavAsync(_sourcePath, start, end);
            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "clip" : NameBox.Text.Trim();
            if (!string.IsNullOrEmpty(_editClipId))
            {
                AppServices.Library.Replace(_editClipId, name, trimmed, (end - start).TotalSeconds);
            }
            else
            {
                AppServices.Library.Add(name, trimmed, (end - start).TotalSeconds);
            }

            WindowHub.OpenLibrary();
            Close();
        }
        catch (Exception ex)
        {
            SourceLabel.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
