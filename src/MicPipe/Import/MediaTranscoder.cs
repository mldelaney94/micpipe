using System.Diagnostics;
using System.Globalization;
using MicPipe.Data;

namespace MicPipe.Import;

public sealed class MediaTranscoder
{
    private readonly ToolResolver _tools;

    public MediaTranscoder(ToolResolver tools)
    {
        _tools = tools;
    }

    public async Task<string> TrimToWavAsync(
        string inputPath,
        TimeSpan start,
        TimeSpan end,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        if (end <= start)
        {
            throw new ArgumentException("End must be after start.");
        }

        outputPath ??= Path.Combine(Path.GetTempPath(), "MicPipe", Guid.NewGuid().ToString("N") + ".wav");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var duration = end - start;
        var args =
            $"-y -ss {Format(start)} -i \"{inputPath}\" -t {Format(duration)} -ac 2 -ar 48000 -c:a pcm_s16le \"{outputPath}\"";

        await RunAsync(_tools.GetFfmpegPath(), args, ct).ConfigureAwait(false);
        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("Couldn't process that audio file.");
        }

        return outputPath;
    }

    public async Task<TimeSpan> GetDurationAsync(string inputPath, CancellationToken ct = default)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(_tools.GetFfmpegPath())!, "ffprobe.exe");
        if (!File.Exists(ffprobe))
        {
            // Fall back: decode via NAudio in caller; here approximate with ffmpeg -i
            return TimeSpan.Zero;
        }

        var args =
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"";
        var output = await RunCaptureAsync(ffprobe, args, ct).ConfigureAwait(false);
        if (double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.Zero;
    }

    private static string Format(TimeSpan t) =>
        t.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

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

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't process that audio file.");
        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            AppLog.Write("Media process failed: " + stderr);
            throw new InvalidOperationException("Couldn't process that audio file.");
        }
    }

    private static async Task<string> RunCaptureAsync(string fileName, string args, CancellationToken ct)
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

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't read that file.");
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return stdout;
    }
}
