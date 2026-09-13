using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using MicPipe.Audio;
using MicPipe.Import;
using MicPipe.Services;

namespace MicPipe.Views;

/// <summary>What to open in the trimmer: a source file, a display name, optionally the clip being edited and a preselected range.</summary>
public sealed record TrimRequest(string SourcePath, string Name, string? EditClipId = null, TimeSpan? Start = null, TimeSpan? End = null);

public sealed partial class TrimWindow : Window
{
    private const double SliderMax = 1000;

    private readonly DispatcherQueueTimer _playTimer;
    private readonly EventHandler<ActiveClip?> _onClipChanged;
    private TrimRequest? _request;
    private TimeSpan _duration;
    private float[] _peaks = [];
    private bool _ready;
    private bool _draggingStart;
    private double? _playhead; // 0–1 within the selection while previewing

    public TrimWindow()
    {
        InitializeComponent();
        WindowSetup.Apply(this, 1080, 816, 768, 624);
        WaveCanvas.SizeChanged += (_, _) => { BuildBars(); DrawOverlays(); };

        _playTimer = DispatcherQueue.CreateTimer();
        _playTimer.Interval = TimeSpan.FromMilliseconds(33);
        _playTimer.Tick += (_, _) =>
        {
            _playhead = AppServices.Engine.GetClipProgress();
            if (_playhead is null)
            {
                _playTimer.Stop();
            }

            DrawOverlays();
        };

        _onClipChanged = (_, clip) => DispatcherQueue.TryEnqueue(() =>
        {
            _playhead = clip is null ? null : 0;
            if (clip is null) _playTimer.Stop(); else _playTimer.Start();
            DrawOverlays();
        });
        AppServices.Engine.ClipChanged += _onClipChanged;
        Closed += (_, _) =>
        {
            _playTimer.Stop();
            AppServices.Engine.ClipChanged -= _onClipChanged;
        };
    }

    public void Load(TrimRequest request)
    {
        _ready = false;
        _request = request;
        _playhead = null;
        _playTimer.Stop();

        var editing = request.EditClipId is not null;
        SourceLabel.Text = editing ? "editing: " + request.Name : "source: " + request.SourcePath;
        SaveButton.Content = editing ? "Save changes" : "Save to library";
        NameBox.Text = request.Name;
        _duration = WaveformBuilder.GetDuration(request.SourcePath);
        _peaks = WaveformBuilder.BuildPeaks(request.SourcePath);

        var startRatio = TimeToRatio(request.Start) ?? 0;
        var endRatio = TimeToRatio(request.End) ?? 1;
        if (endRatio <= startRatio)
        {
            endRatio = Math.Min(1, startRatio + 0.01);
        }

        StartSlider.Value = startRatio * SliderMax;
        EndSlider.Value = endRatio * SliderMax;
        GainSlider.Value = 100;
        _ready = true;

        UpdateLabels();
        BuildBars();
        DrawOverlays();
    }

    private double? TimeToRatio(TimeSpan? t) =>
        t is TimeSpan ts && ts > TimeSpan.Zero && _duration > TimeSpan.Zero
            ? Math.Clamp(ts.TotalSeconds / _duration.TotalSeconds, 0, 1)
            : null;

    private TimeSpan RatioToTime(double ratio) => TimeSpan.FromTicks((long)(_duration.Ticks * Math.Clamp(ratio, 0, 1)));

    private TimeSpan SelectionStart => RatioToTime(StartSlider.Value / SliderMax);
    private TimeSpan SelectionEnd => RatioToTime(EndSlider.Value / SliderMax);
    private double CurrentGain => GainSlider.Value / 100.0;
    private string ClipName => string.IsNullOrWhiteSpace(NameBox.Text) ? "clip" : NameBox.Text.Trim();

    private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";

    private void Gain_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_ready) GainLabel.Text = $"{(int)e.NewValue}%";
    }

    private void Range_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        if (EndSlider.Value <= StartSlider.Value)
        {
            if (ReferenceEquals(sender, StartSlider))
                EndSlider.Value = Math.Min(SliderMax, StartSlider.Value + 1);
            else
                StartSlider.Value = Math.Max(0, EndSlider.Value - 1);
        }

        UpdateLabels();
        DrawOverlays();
    }

    private void UpdateLabels()
    {
        StartLabel.Text = Format(SelectionStart);
        EndLabel.Text = Format(_duration);
        SelectionLabel.Text = "selection " + Format(SelectionEnd - SelectionStart);
    }

    private void BuildBars()
    {
        WaveBars.Children.Clear();
        var w = WaveCanvas.ActualWidth;
        var h = WaveCanvas.ActualHeight;
        if (_peaks.Length == 0 || w <= 0)
        {
            return;
        }

        var barBrush = (Brush)((FrameworkElement)Content).Resources["WaveBarBrush"];
        var barWidth = Math.Max(1, w / _peaks.Length);
        for (var i = 0; i < _peaks.Length; i++)
        {
            var amp = _peaks[i] * (h * 0.45);
            var bar = new Rectangle { Width = barWidth, Height = Math.Max(1, amp * 2), Fill = barBrush };
            Canvas.SetLeft(bar, i * barWidth);
            Canvas.SetTop(bar, h / 2 - amp);
            WaveBars.Children.Add(bar);
        }
    }

    private void DrawOverlays()
    {
        var w = WaveCanvas.ActualWidth;
        var h = WaveCanvas.ActualHeight;
        var startX = w * (StartSlider.Value / SliderMax);
        var endX = w * (EndSlider.Value / SliderMax);

        SelectionBand.Height = h;
        SelectionBand.Width = Math.Max(1, endX - startX);
        Canvas.SetLeft(SelectionBand, startX);

        PlayedBand.Visibility = Playhead.Visibility = _playhead is null ? Visibility.Collapsed : Visibility.Visible;
        if (_playhead is double p)
        {
            var playX = startX + (endX - startX) * Math.Clamp(p, 0, 1);
            PlayedBand.Height = Playhead.Height = h;
            PlayedBand.Width = Math.Max(1, playX - startX);
            Canvas.SetLeft(PlayedBand, startX);
            Canvas.SetLeft(Playhead, playX - 1);
        }
    }

    private void Wave_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var ratio = PointerRatio(e);
        _draggingStart = Math.Abs(ratio - StartSlider.Value) <= Math.Abs(ratio - EndSlider.Value);
        MoveHandle(ratio);
    }

    private void Wave_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.IsInContact) MoveHandle(PointerRatio(e));
    }

    private double PointerRatio(PointerRoutedEventArgs e) =>
        e.GetCurrentPoint(WaveCanvas).Position.X / Math.Max(1, WaveCanvas.ActualWidth) * SliderMax;

    private void MoveHandle(double ratio)
    {
        if (_draggingStart) StartSlider.Value = ratio; else EndSlider.Value = ratio;
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_request is null) return;
        try
        {
            var trimmed = await MediaTranscoder.TrimToWavAsync(_request.SourcePath, SelectionStart, SelectionEnd, CurrentGain);
            AppServices.Engine.PreviewClip(trimmed, ClipName);
        }
        catch (Exception ex)
        {
            SourceLabel.Text = ex.Message;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_request is null) return;
        try
        {
            var start = SelectionStart;
            var end = SelectionEnd;
            var trimmed = await MediaTranscoder.TrimToWavAsync(_request.SourcePath, start, end, CurrentGain);
            var seconds = (end - start).TotalSeconds;
            if (_request.EditClipId is string id)
                AppServices.Library.Replace(id, ClipName, trimmed, seconds);
            else
                AppServices.Library.Add(ClipName, trimmed, seconds);

            WindowHub.OpenLibrary();
            Close();
        }
        catch (Exception ex)
        {
            SourceLabel.Text = ex.Message;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => AppServices.Engine.StopClip();
}
