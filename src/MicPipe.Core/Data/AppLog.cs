namespace MicPipe.Data;

/// <summary>Append-only diagnostics log at %LocalAppData%\MicPipe\micpipe.log. Never throws.</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(AppSettings.RootDir, "micpipe.log");

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.RootDir);
            var line = $"{DateTimeOffset.Now:O} {message}";
            if (ex is not null)
            {
                line += Environment.NewLine + ex;
            }

            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }
}
