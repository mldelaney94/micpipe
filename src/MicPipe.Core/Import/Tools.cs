namespace MicPipe.Import;

/// <summary>
/// Locates the bundled media tools: <c>tools\{name}\{name}.exe</c> next to the app, or in an
/// ancestor folder when running from the source tree (bin\x64\Debug\... → repo root).
/// </summary>
public static class Tools
{
    private static readonly Lazy<string> FfmpegPath = new(() => Resolve("ffmpeg", "ffmpeg.exe"));
    private static readonly Lazy<string> YtDlpPath = new(() => Resolve("yt-dlp", "yt-dlp.exe"));

    public static string Ffmpeg => FfmpegPath.Value;
    public static string YtDlp => YtDlpPath.Value;

    private static string Resolve(string folder, string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", folder, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("A required media component is missing from this install. Reinstall MicPipe.", file);
    }
}
