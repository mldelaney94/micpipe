using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using MicPipe.Import;
using MicPipe.Services;

namespace MicPipe.Views;

public sealed partial class ImportWindow : Window
{
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".flac", ".m4a", ".ogg", ".wma", ".aac"];

    public ImportWindow()
    {
        InitializeComponent();
        WindowSetup.Apply(this, 672, 624, 576, 480);
    }

    private async void FromFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(AppWindow.Id);
        foreach (var ext in AudioExtensions)
        {
            picker.FileTypeFilter.Add(ext);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        WindowHub.OpenTrim(new TrimRequest(file.Path, Path.GetFileNameWithoutExtension(file.Path)));
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
            var result = await UrlAudioFetcher.FetchAsync(
                UrlBox.Text.Trim(), TimeParser.Parse(FromBox.Text), TimeParser.Parse(ToBox.Text));
            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "clip" : NameBox.Text.Trim();
            WindowHub.OpenTrim(new TrimRequest(result.Path, name, Start: result.TrimStart, End: result.TrimEnd));
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
}
