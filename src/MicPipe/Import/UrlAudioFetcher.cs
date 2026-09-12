using System.Diagnostics;
using System.Globalization;
using MicPipe.Data;

namespace MicPipe.Import;

public sealed class UrlFetchResult
{
    public required string Path { get; init; }

    /// <summary>Suggested selection start within the downloaded file (after pad).</summary>
    public TimeSpan? SuggestedTrimStart { get; init; }

    /// <summary>Suggested selection end within the downloaded file (after pad).</summary>
    public TimeSpan? SuggestedTrimEnd { get; init; }
}

public sealed class UrlAudioFetcher
{
    /// <summary>Extra media fetched before/after the requested section so trim edits need fewer re-fetches.</summary>
    public const double SectionPadSeconds = 2;

    private readonly ToolResolver _tools;

    public UrlAudioFetcher(ToolResolver tools)
    {
        _tools = tools;
    }

    public async Task<UrlFetchResult> FetchAsync(
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
        var ffmpegDir = Path.GetDirectoryName(_tools.GetFfmpegPath())!;

        var args = new List<string>
        {
            "--no-playlist",
            "-f", "bestaudio/best",
            "-o", Quote(outTemplate),
            "--ffmpeg-location", Quote(ffmpegDir),
            "--no-progress"
        };

        TimeSpan? suggestedStart = null;
        TimeSpan? suggestedEnd = null;

        if (start is not null || end is not null)
        {
            var (fetchStart, fetchEnd, trimStart, trimEnd) = ApplySectionPad(start, end);
            suggestedStart = trimStart;
            suggestedEnd = trimEnd;

            args.Add("--download-sections");
            args.Add(Quote("*" + FormatSection(fetchStart, fetchEnd)));
            args.Add("--force-keyframes-at-cuts");
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

        var file = Directory.GetFiles(workDir)
            .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (file is null)
        {
            throw new InvalidOperationException("Couldn't fetch that URL.");
        }

        return new UrlFetchResult
        {
            Path = file,
            SuggestedTrimStart = suggestedStart,
            SuggestedTrimEnd = suggestedEnd
        };
    }

    /// <summary>
    /// Expands the requested range by <see cref="SectionPadSeconds"/> on each side (start floored at 0).
    /// Returns fetch bounds plus the original range mapped into the padded file.
    /// </summary>
    internal static (TimeSpan FetchStart, TimeSpan? FetchEnd, TimeSpan TrimStart, TimeSpan? TrimEnd)
        ApplySectionPad(TimeSpan? start, TimeSpan? end)
    {
        var pad = TimeSpan.FromSeconds(SectionPadSeconds);
        var requestedStart = start ?? TimeSpan.Zero;
        if (requestedStart < TimeSpan.Zero)
        {
            requestedStart = TimeSpan.Zero;
        }

        var fetchStart = requestedStart > pad ? requestedStart - pad : TimeSpan.Zero;
        TimeSpan? fetchEnd = end is null ? null : end.Value + pad;

        // Original range relative to the start of the downloaded file
        var trimStart = requestedStart - fetchStart;
        TimeSpan? trimEnd = end is null ? null : end.Value - fetchStart;

        return (fetchStart, fetchEnd, trimStart, trimEnd);
    }

    private static string FormatSection(TimeSpan start, TimeSpan? end)
    {
        var startSec = start.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        if (end is null)
        {
            return startSec + "-inf";
        }

        var endSec = end.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return startSec + "-" + endSec;
    }

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
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            AppLog.Write("URL tool failed: " + stderr);
            throw new InvalidOperationException("Couldn't fetch that URL.");
        }
    }
}
