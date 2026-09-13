using MicPipe.Data;

namespace MicPipe.Tests;

/// <summary>
/// Points AppSettings/ClipLibrary/AppLog at a fresh temp folder for one test. Disposing falls back to a
/// process-wide sandbox, never to the user's real %LocalAppData%\MicPipe, so a stray Save() cannot reach it.
/// </summary>
public sealed class TempAppRoot : IDisposable
{
    private static readonly string Sandbox = Path.Combine(Path.GetTempPath(), "MicPipe.Tests", "sandbox");

    static TempAppRoot()
    {
        Directory.CreateDirectory(Sandbox);
        AppSettings.RootDirOverride = Sandbox;
    }

    public string Root { get; }

    public TempAppRoot()
    {
        Root = Path.Combine(Path.GetTempPath(), "MicPipe.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        AppSettings.RootDirOverride = Root;
    }

    public void Dispose()
    {
        AppSettings.RootDirOverride = Sandbox;
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
