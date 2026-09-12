using System.Globalization;
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
        AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 360));
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
    }

    private async void Fetch_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Fetching…";
        try
        {
            TimeSpan? start = ParseTime(FromBox.Text);
            TimeSpan? end = ParseTime(ToBox.Text);
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
    }

    private static TimeSpan? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm\:ss", @"m\:ss", @"mm\:ss", @"hh\:mm\:ss" },
                CultureInfo.InvariantCulture, out var ts))
        {
            return ts;
        }

        if (TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out ts))
        {
            return ts;
        }

        return null;
    }
}
