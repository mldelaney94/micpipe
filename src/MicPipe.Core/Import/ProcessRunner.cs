using System.Diagnostics;
using MicPipe.Data;

namespace MicPipe.Import;

internal static class ProcessRunner
{
    /// <summary>
    /// Runs a bundled tool to completion. On a non-zero exit, stderr goes to the log and
    /// <paramref name="userError"/> is thrown so the UI never shows tool internals.
    /// </summary>
    public static async Task RunAsync(string fileName, IEnumerable<string> args, string userError, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException(userError);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        await stdout.ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            AppLog.Write($"{Path.GetFileName(fileName)} exited {proc.ExitCode}: {await stderr.ConfigureAwait(false)}");
            throw new InvalidOperationException(userError);
        }
    }
}
