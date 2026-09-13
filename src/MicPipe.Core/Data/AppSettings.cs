using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicPipe.Data;

/// <summary>Persisted user settings (%LocalAppData%\MicPipe\settings.json).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string? MicDeviceId { get; set; }
    public string? CableOutputDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public float MicVolume { get; set; } = 0.7f;
    public float ClipVolume { get; set; } = 0.85f;
    public List<ClipKeybind> ClipKeybinds { get; set; } = new();

    /// <summary>Win32 virtual-key code (or mouse button 3/4/5 when <see cref="PushToTalkIsMouse"/>) held while clips play. Null = off.</summary>
    public int? PushToTalkVirtualKey { get; set; }
    public bool PushToTalkIsMouse { get; set; }
    public string? PushToTalkKeyName { get; set; }

    /// <summary>Pre-<see cref="ClipKeybinds"/> store ("F1" → clip id). Read for migration only; never written back.</summary>
    public Dictionary<string, string>? HotkeyBindings { get; set; }

    [JsonIgnore]
    public bool NeedsDeviceSetup =>
        string.IsNullOrWhiteSpace(MicDeviceId) || string.IsNullOrWhiteSpace(CableOutputDeviceId);

    /// <summary>When set (tests), settings/clips/logs live under this folder instead of LocalAppData.</summary>
    public static string? RootDirOverride { get; set; }

    public static string RootDir =>
        RootDirOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicPipe");

    public static string SettingsPath => Path.Combine(RootDir, "settings.json");

    public static AppSettings Load()
    {
        Directory.CreateDirectory(RootDir);
        var settings = new AppSettings();
        if (File.Exists(SettingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? settings;
            }
            catch (Exception ex)
            {
                AppLog.Write("settings.json unreadable; using defaults", ex);
            }
        }

        if (settings.MigrateLegacyBindings())
        {
            settings.Save();
        }

        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(RootDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Folds the old F-key dictionary into <see cref="ClipKeybinds"/>. Returns true if anything changed.</summary>
    internal bool MigrateLegacyBindings()
    {
        if (HotkeyBindings is null)
        {
            return false;
        }

        foreach (var (keyName, clipId) in HotkeyBindings)
        {
            if (string.IsNullOrWhiteSpace(clipId) || ClipKeybind.ParseFKey(keyName) is not int n)
            {
                continue;
            }

            var code = ClipKeybind.FKeyCode(n);
            if (ClipKeybinds.Any(b => b.ClipId == clipId || (!b.IsMouse && b.Code == code)))
            {
                continue;
            }

            ClipKeybinds.Add(new ClipKeybind { ClipId = clipId, Code = code, Label = "F" + n });
        }

        HotkeyBindings = null;
        return true;
    }
}

/// <summary>A global key or mouse button that plays one clip.</summary>
public sealed class ClipKeybind
{
    public string ClipId { get; set; } = "";

    /// <summary>Virtual-key code, or mouse button 3/4/5 when <see cref="IsMouse"/>.</summary>
    public int Code { get; set; }
    public bool IsMouse { get; set; }
    public string Label { get; set; } = "";

    private const int VkF1 = 0x70;

    public static int FKeyCode(int n) => VkF1 + n - 1;

    /// <summary>1–24 when <paramref name="code"/> is a function key, else null.</summary>
    public static int? FKeyNumber(int code) => code is >= VkF1 and < VkF1 + 24 ? code - VkF1 + 1 : null;

    /// <summary>"F7" → 7; anything else → null.</summary>
    public static int? ParseFKey(string? label) =>
        label is { Length: >= 2 and <= 3 } && (label[0] is 'F' or 'f') &&
        int.TryParse(label.AsSpan(1), out var n) && n is >= 1 and <= 24
            ? n
            : null;

    public bool IsFKey => !IsMouse && FKeyNumber(Code) is not null;
}
