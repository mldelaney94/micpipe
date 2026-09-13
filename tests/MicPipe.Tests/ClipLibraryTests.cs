using MicPipe.Data;

namespace MicPipe.Tests;

public class ClipLibraryTests
{
    [Fact]
    public void Add_replace_delete_round_trip()
    {
        using var root = new TempAppRoot();
        var lib = ClipLibrary.Load();
        Assert.Empty(lib.Clips);

        var src = Path.Combine(root.Root, "a.wav");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3, 4 });

        var entry = lib.Add("hello", src, durationSeconds: 1.5);
        Assert.Single(lib.Clips);
        Assert.True(File.Exists(lib.GetPath(entry)));

        var src2 = Path.Combine(root.Root, "b.wav");
        File.WriteAllBytes(src2, new byte[] { 9, 9, 9 });
        var replaced = lib.Replace(entry.Id, "hello2", src2, 2.0);
        Assert.NotNull(replaced);
        Assert.Equal("hello2", replaced!.Name);
        Assert.Equal(2.0, replaced.DurationSeconds);

        lib.Delete(entry.Id);
        Assert.Empty(lib.Clips);
        Assert.False(File.Exists(lib.GetPath(entry)));
    }
}
