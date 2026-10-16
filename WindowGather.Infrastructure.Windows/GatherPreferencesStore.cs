using System.Text.Json;

namespace WindowGather;

public sealed class GatherPreferencesStore(string directory) : IGatherPreferencesStore
{
    private readonly string path = Path.Combine(directory, "gather-options.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public GatherPreferences Load()
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("The gathering options file is too large.");
        GatherPreferences preferences = JsonSerializer.Deserialize<GatherPreferences>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The gathering options file is empty.");
        preferences.Validate();
        return preferences;
    }

    public void Save(GatherPreferences preferences)
    {
        preferences.Validate();
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(JsonSerializer.SerializeToUtf8Bytes(preferences, Options));
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
