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
    private TimeSpan _duration;
    private float[] _peaks = Array.Empty<float>();
    private bool _ready;
    private bool _draggingStart;

    public TrimWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(720, 420));
        WaveCanvas.SizeChanged += (_, _) => DrawWaveform();
    }

    public void LoadSource(string path, string? suggestedName)
    {
        _sourcePath = path;
        SourceLabel.Text = "source: " + path;
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
        WaveCanvas.Children.Add(new Rectangle
        {
            Width = Math.Max(1, endX - startX),
            Height = h,
            Fill = new SolidColorBrush(Color.FromArgb(60, 0xE0, 0xA0, 0x45))
        });
        Canvas.SetLeft(WaveCanvas.Children[^1], startX);

        for (var i = 0; i < _peaks.Length; i++)
        {
            var amp = _peaks[i] * (h * 0.45);
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(1, amp * 2),
                Fill = new SolidColorBrush(Color.FromArgb(255, 0xE8, 0xEA, 0xED))
            };
            Canvas.SetLeft(rect, i * barWidth);
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
            AppServices.Engine.PlayClip(trimmed, NameBox.Text);
        }
        catch (Exception ex)
        {
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
            AppServices.Library.Add(name, trimmed, (end - start).TotalSeconds);
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
