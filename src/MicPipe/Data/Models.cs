using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicPipe.Data;

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
    public bool HotkeyHoldToPlay { get; set; }
    public Dictionary<string, string> HotkeyBindings { get; set; } = new();
    public List<ClipKeybind> ClipKeybinds { get; set; } = new();
    public bool FirstRunComplete { get; set; }

    /// <summary>Win32 virtual-key code held while clips play (push-to-talk). Null/0 = off.</summary>
    public int? PushToTalkVirtualKey { get; set; }

    /// <summary>When true, PushToTalkVirtualKey is a mouse button: 4 = Mouse4 (X1), 5 = Mouse5 (X2), 3 = Middle.</summary>
    public bool PushToTalkIsMouse { get; set; }

    /// <summary>Display name for the PTT binding (e.g. "V" or "Mouse4").</summary>
    public string? PushToTalkKeyName { get; set; }

    public bool NeedsDeviceSetup =>
        string.IsNullOrWhiteSpace(MicDeviceId) || string.IsNullOrWhiteSpace(CableOutputDeviceId);

    public static string RootDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicPipe");

    public static string SettingsPath => Path.Combine(RootDir, "settings.json");

    public static AppSettings Load()
    {
        Directory.CreateDirectory(RootDir);
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(RootDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}

public sealed class ClipKeybind
{
    public string ClipId { get; set; } = "";
    /// <summary>Virtual-key code, or mouse button 3/4/5 when IsMouse.</summary>
    public int Code { get; set; }
    public bool IsMouse { get; set; }
    public string Label { get; set; } = "";
}

public sealed class ClipEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public double DurationSeconds { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClipLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly List<ClipEntry> _clips = new();

    public IReadOnlyList<ClipEntry> Clips => _clips;

    public static string LibraryDir => Path.Combine(AppSettings.RootDir, "clips");
    public static string IndexPath => Path.Combine(AppSettings.RootDir, "library.json");

    public static ClipLibrary Load()
    {
        Directory.CreateDirectory(LibraryDir);
        var lib = new ClipLibrary();
        if (!File.Exists(IndexPath))
        {
            return lib;
        }

        try
        {
            var json = File.ReadAllText(IndexPath);
            var items = JsonSerializer.Deserialize<List<ClipEntry>>(json, JsonOptions);
            if (items is not null)
            {
                lib._clips.AddRange(items);
            }
        }
        catch
        {
            // keep empty
        }

        return lib;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppSettings.RootDir);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(_clips, JsonOptions));
    }

    public ClipEntry Add(string name, string sourcePath, double durationSeconds)
    {
        Directory.CreateDirectory(LibraryDir);
        var id = Guid.NewGuid().ToString("N");
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = ".wav";
        }

        var fileName = id + ext.ToLowerInvariant();
        var dest = Path.Combine(LibraryDir, fileName);
        File.Copy(sourcePath, dest, overwrite: true);

        var entry = new ClipEntry
        {
            Id = id,
            Name = name,
            FileName = fileName,
            DurationSeconds = durationSeconds
        };
        _clips.Add(entry);
        Save();
        return entry;
    }

    public string GetPath(ClipEntry entry) => Path.Combine(LibraryDir, entry.FileName);

    public bool TryGet(string id, out ClipEntry? entry)
    {
        entry = _clips.FirstOrDefault(c => c.Id == id);
        return entry is not null;
    }

    public void Rename(string id, string name)
    {
        var entry = _clips.FirstOrDefault(c => c.Id == id);
        if (entry is null)
        {
            return;
        }

        entry.Name = name;
        Save();
    }

    public ClipEntry? Replace(string id, string name, string sourcePath, double durationSeconds)
    {
        var entry = _clips.FirstOrDefault(c => c.Id == id);
        if (entry is null)
        {
            return null;
        }

        Directory.CreateDirectory(LibraryDir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = ".wav";
        }

        var fileName = id + ext.ToLowerInvariant();
        var dest = Path.Combine(LibraryDir, fileName);
        File.Copy(sourcePath, dest, overwrite: true);

        // Remove old file if extension changed
        if (!string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            var old = Path.Combine(LibraryDir, entry.FileName);
            if (File.Exists(old))
            {
                try { File.Delete(old); } catch { /* ignore */ }
            }
        }

        entry.Name = name;
        entry.FileName = fileName;
        entry.DurationSeconds = durationSeconds;
        Save();
        return entry;
    }

    public void Delete(string id)
    {
        var entry = _clips.FirstOrDefault(c => c.Id == id);
        if (entry is null)
        {
            return;
        }

        _clips.Remove(entry);
        var path = GetPath(entry);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }

        Save();
    }
}

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
            // never throw from logging
        }
    }
}
