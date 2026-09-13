using System.Text.Json;

namespace MicPipe.Data;

public sealed class ClipEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public double DurationSeconds { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Clip files under %LocalAppData%\MicPipe\clips plus a JSON index. Every mutation saves.</summary>
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
        if (File.Exists(IndexPath))
        {
            try
            {
                lib._clips.AddRange(JsonSerializer.Deserialize<List<ClipEntry>>(File.ReadAllText(IndexPath), JsonOptions) ?? []);
            }
            catch (Exception ex)
            {
                AppLog.Write("library.json unreadable; starting empty", ex);
            }
        }

        return lib;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppSettings.RootDir);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(_clips, JsonOptions));
    }

    public ClipEntry? Find(string id) => _clips.FirstOrDefault(c => c.Id == id);

    public string GetPath(ClipEntry entry) => Path.Combine(LibraryDir, entry.FileName);

    public ClipEntry Add(string name, string sourcePath, double durationSeconds)
    {
        var entry = new ClipEntry { Name = name, DurationSeconds = durationSeconds };
        entry.FileName = CopyIn(entry.Id, sourcePath);
        _clips.Add(entry);
        Save();
        return entry;
    }

    /// <summary>Swaps in a new audio file for an existing clip (edit in scrubber).</summary>
    public ClipEntry? Replace(string id, string name, string sourcePath, double durationSeconds)
    {
        if (Find(id) is not ClipEntry entry)
        {
            return null;
        }

        var fileName = CopyIn(id, sourcePath);
        if (!string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(Path.Combine(LibraryDir, entry.FileName));
        }

        entry.Name = name;
        entry.FileName = fileName;
        entry.DurationSeconds = durationSeconds;
        Save();
        return entry;
    }

    public void Rename(string id, string name)
    {
        if (Find(id) is ClipEntry entry)
        {
            entry.Name = name;
            Save();
        }
    }

    public void Delete(string id)
    {
        if (Find(id) is not ClipEntry entry)
        {
            return;
        }

        _clips.Remove(entry);
        TryDelete(GetPath(entry));
        Save();
    }

    /// <summary>Copies <paramref name="sourcePath"/> into the library as "{id}{ext}" and returns that file name.</summary>
    private static string CopyIn(string id, string sourcePath)
    {
        Directory.CreateDirectory(LibraryDir);
        var ext = Path.GetExtension(sourcePath);
        var fileName = id + (string.IsNullOrWhiteSpace(ext) ? ".wav" : ext.ToLowerInvariant());
        File.Copy(sourcePath, Path.Combine(LibraryDir, fileName), overwrite: true);
        return fileName;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Write("Could not delete " + path, ex);
        }
    }
}
