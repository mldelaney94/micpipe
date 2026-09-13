using MicPipe.Data;

namespace MicPipe.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Load_migrates_legacy_HotkeyBindings_into_ClipKeybinds_and_saves()
    {
        using var root = new TempAppRoot();
        File.WriteAllText(AppSettings.SettingsPath, """
            {
              "hotkeyBindings": { "F1": "clip-a", "F12": "clip-b", "F3": "", "Q": "clip-c" },
              "clipKeybinds": [ { "clipId": "clip-b", "code": 4, "isMouse": true, "label": "Mouse4" } ]
            }
            """);

        var settings = AppSettings.Load();

        Assert.Null(settings.HotkeyBindings);
        Assert.Equal(2, settings.ClipKeybinds.Count);
        Assert.Contains(settings.ClipKeybinds, b => b.ClipId == "clip-a" && b.Code == ClipKeybind.FKeyCode(1) && b.Label == "F1" && !b.IsMouse);
        // clip-b already had a bind, so its legacy F12 is dropped (one bind per clip); "Q" is not an F-key.
        Assert.Single(settings.ClipKeybinds, b => b.ClipId == "clip-b");
        Assert.DoesNotContain(settings.ClipKeybinds, b => b.ClipId == "clip-c");

        var reloaded = AppSettings.Load();
        Assert.Null(reloaded.HotkeyBindings);
        Assert.Equal(2, reloaded.ClipKeybinds.Count);
    }

    [Fact]
    public void Missing_file_yields_defaults_and_needs_setup()
    {
        using var root = new TempAppRoot();
        var settings = AppSettings.Load();

        Assert.True(settings.NeedsDeviceSetup);
        Assert.Equal(0.7f, settings.MicVolume);
        Assert.Empty(settings.ClipKeybinds);
    }

    [Theory]
    [InlineData("F1", 1)]
    [InlineData("f12", 12)]
    [InlineData("F0", null)]
    [InlineData("F24", 24)]
    [InlineData("F25", null)]
    [InlineData("Q", null)]
    [InlineData(null, null)]
    public void ParseFKey_accepts_F1_to_F24_only(string? label, int? expected)
    {
        Assert.Equal(expected, ClipKeybind.ParseFKey(label));
    }
}
