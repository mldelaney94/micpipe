using System.Globalization;

namespace MicPipe.Import;

/// <param name="Path">Downloaded audio file.</param>
/// <param name="TrimStart">Where the user's requested range starts inside the (padded) file.</param>
/// <param name="TrimEnd">Where it ends, or null for "to the end".</param>
public sealed record UrlFetchResult(string Path, TimeSpan? TrimStart, TimeSpan? TrimEnd);

public static class UrlAudioFetcher
{
    /// <summary>Extra media fetched either side of a requested section so small trim edits need no re-download.</summary>
    public const double SectionPadSeconds = 2;

    public static async Task<UrlFetchResult> FetchAsync(string url, TimeSpan? start, TimeSpan? end, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL is required.");
        }

        var workDir = Path.Combine(Path.GetTempPath(), "MicPipe", "fetch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var args = new List<string>
        {
            "--no-playlist", "--no-progress",
            "-f", "bestaudio/best",
            "-o", Path.Combine(workDir, "audio.%(ext)s"),
            "--ffmpeg-location", Path.GetDirectoryName(Tools.Ffmpeg)!
        };

        TimeSpan? trimStart = null, trimEnd = null;
        if (start is not null || end is not null)
        {
            var pad = ApplySectionPad(start, end);
            (trimStart, trimEnd) = (pad.TrimStart, pad.TrimEnd);
            args.AddRange(["--download-sections", "*" + FormatSection(pad.FetchStart, pad.FetchEnd), "--force-keyframes-at-cuts"]);
        }

        args.Add(url);
        await ProcessRunner.RunAsync(Tools.YtDlp, args, "Couldn't fetch that URL.", ct).ConfigureAwait(false);

        var file = Directory.GetFiles(workDir)
            .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? throw new InvalidOperationException("Couldn't fetch that URL.");

        return new UrlFetchResult(file, trimStart, trimEnd);
    }

    /// <summary>Widens the range by <see cref="SectionPadSeconds"/> each side (floored at 0) and maps the original range into the padded file.</summary>
    public static (TimeSpan FetchStart, TimeSpan? FetchEnd, TimeSpan TrimStart, TimeSpan? TrimEnd) ApplySectionPad(TimeSpan? start, TimeSpan? end)
    {
        var pad = TimeSpan.FromSeconds(SectionPadSeconds);
        var requestedStart = start is { } s && s > TimeSpan.Zero ? s : TimeSpan.Zero;
        var fetchStart = requestedStart > pad ? requestedStart - pad : TimeSpan.Zero;
        return (fetchStart, end + pad, requestedStart - fetchStart, end - fetchStart);
    }

    private static string FormatSection(TimeSpan start, TimeSpan? end)
    {
        var s = start.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return end is null ? s + "-inf" : s + "-" + end.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
