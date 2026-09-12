using MicPipe.Data;

namespace MicPipe.Import;

public sealed class ToolResolver
{
    private string? _ffmpeg;
    private string? _ytDlp;

    public string GetFfmpegPath()
    {
        _ffmpeg ??= Resolve("ffmpeg", "ffmpeg.exe");
        return _ffmpeg;
    }

    public string GetYtDlpPath()
    {
        _ytDlp ??= Resolve("yt-dlp", "yt-dlp.exe");
        return _ytDlp;
    }

    private static string Resolve(string folderName, string fileName)
    {
        foreach (var path in CandidatePaths(folderName, fileName))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Mirror into LocalAppData runtime if we can find a sibling install copy
        var runtimeDest = Path.Combine(AppSettings.RootDir, "runtime", folderName, fileName);
        foreach (var path in CandidatePaths(folderName, fileName))
        {
            // already checked
        }

        throw new FileNotFoundException(
            "A required media component is missing from this install. Reinstall MicPipe.",
            runtimeDest);
    }

    private static IEnumerable<string> CandidatePaths(string folderName, string fileName)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "tools", folderName, fileName);
        yield return Path.Combine(AppSettings.RootDir, "runtime", folderName, fileName);

        // Dev tree: src/MicPipe/bin/x64/Debug/netX/... -> ../../../../../../tools
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            yield return Path.Combine(dir.FullName, "tools", folderName, fileName);
            dir = dir.Parent;
        }
    }
}
