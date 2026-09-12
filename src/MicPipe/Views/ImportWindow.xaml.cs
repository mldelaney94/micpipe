using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MicPipe.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MicPipe.Views;

public sealed partial class ImportWindow : Window
{
    public ImportWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(672, 624));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 576;
            presenter.PreferredMinimumHeight = 480;
        }

        TrySetWindowIcon();
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MicPipe.ico");
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

    private async void FromFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".m4a");
        picker.FileTypeFilter.Add(".ogg");
        picker.FileTypeFilter.Add(".wma");
        picker.FileTypeFilter.Add(".aac");
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        WindowHub.OpenTrim(file.Path, Path.GetFileNameWithoutExtension(file.Name));
        Close();
    }

    private void FromUrl_Click(object sender, RoutedEventArgs e)
    {
        UrlPanel.Visibility = Visibility.Visible;
        FetchButton.Visibility = Visibility.Visible;
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Fetching…";
        FetchButton.IsEnabled = false;
        try
        {
            var start = ParseTime(FromBox.Text);
            var end = ParseTime(ToBox.Text);
            var path = await AppServices.UrlFetcher.FetchAsync(UrlBox.Text.Trim(), start, end);
            var name = string.IsNullOrWhiteSpace(NameBox.Text)
                ? "clip"
                : NameBox.Text.Trim();
            WindowHub.OpenTrim(path, name);
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            FetchButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Plain numbers are seconds (e.g. 6 → 6s). Also accepts m:ss / h:mm:ss.
    /// </summary>
    internal static TimeSpan? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var raw = text.Trim();

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            !raw.Contains(':') && !raw.Contains('.'))
        {
            // Integer-like plain number → seconds. "6" must not become 6 days via TimeSpan.TryParse.
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
            {
                return TimeSpan.FromSeconds(whole);
            }

            return TimeSpan.FromSeconds(seconds);
        }

        // Allow "6.5" as fractional seconds
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) &&
            !raw.Contains(':'))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (TimeSpan.TryParseExact(raw, new[] { @"h\:mm\:ss", @"m\:ss", @"mm\:ss", @"hh\:mm\:ss", @"h\:mm\:ss\.FFF", @"m\:ss\.FFF" },
                CultureInfo.InvariantCulture, out var ts))
        {
            return ts;
        }

        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out ts))
        {
            return ts;
        }

        return null;
    }
}
