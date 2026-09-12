using System.Diagnostics;
using System.Globalization;
using MicPipe.Data;

namespace MicPipe.Import;

public sealed class UrlAudioFetcher
{
    private readonly ToolResolver _tools;

    public UrlAudioFetcher(ToolResolver tools)
    {
        _tools = tools;
    }

    public async Task<string> FetchAsync(
        string url,
        TimeSpan? start,
        TimeSpan? end,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL is required.");
        }

        var workDir = Path.Combine(Path.GetTempPath(), "MicPipe", "fetch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var outTemplate = Path.Combine(workDir, "audio.%(ext)s");

        var args = new List<string>
        {
            "--no-playlist",
            "-f", "bestaudio/best",
            "-o", Quote(outTemplate),
            "--no-progress",
            "--quiet"
        };

        if (start is not null || end is not null)
        {
            var section = FormatSection(start, end);
            args.Add("--download-sections");
            args.Add(Quote("*" + section));
        }

        args.Add(Quote(url));

        try
        {
            await RunAsync(_tools.GetYtDlpPath(), string.Join(' ', args), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Write("URL fetch failed", ex);
            throw new InvalidOperationException("Couldn't fetch that URL.", ex);
        }

        var file = Directory.GetFiles(workDir).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (file is null)
        {
            throw new InvalidOperationException("Couldn't fetch that URL.");
        }

        return file;
    }

    private static string FormatSection(TimeSpan? start, TimeSpan? end)
    {
        var a = start ?? TimeSpan.Zero;
        if (end is null)
        {
            return Format(a) + "-";
        }

        return Format(a) + "-" + Format(end.Value);
    }

    private static string Format(TimeSpan t) =>
        ((int)t.TotalHours).ToString("00", CultureInfo.InvariantCulture) + t.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static async Task RunAsync(string fileName, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't fetch that URL.");
        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            AppLog.Write("URL tool failed: " + stderr);
            throw new InvalidOperationException("Couldn't fetch that URL.");
        }
    }
}
