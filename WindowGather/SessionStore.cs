using System.Text.Json;

namespace WindowGather;

public sealed class SessionStore(string directory) : ISessionStore
{
    private readonly string path = Path.Combine(directory, "session.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public GatherSession? Load()
    {
        if (!File.Exists(path)) return null;
        var file = new FileInfo(path);
        if (file.Length > 16 * 1024 * 1024)
            throw new InvalidDataException($"The recovery file is too large: {path}");
        var session = JsonSerializer.Deserialize<GatherSession>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"The recovery file is empty: {path}");
        if (session.Version != 1 || !Guid.TryParse(session.Token, out _) ||
            session.Windows is null || session.Windows.Count > 10000 ||
            session.Target is null || string.IsNullOrWhiteSpace(session.Target.Id) ||
            session.Windows.Any(w => w is null || w.Handle == 0 || w.ProcessId <= 0 ||
                w.ProcessStartedUtcTicks <= 0 || string.IsNullOrWhiteSpace(w.ClassName) ||
                w.Placement is null || w.Placement.Normal.Width <= 0 ||
                w.Placement.Normal.Height <= 0 || w.Origin is null ||
                string.IsNullOrWhiteSpace(w.Origin.Id) || w.Origin.WorkArea.Width <= 0 ||
                w.Origin.WorkArea.Height <= 0 || w.Origin.Id == session.Target.Id))
            throw new InvalidDataException($"The recovery file has invalid data: {path}");
        return session;
    }

    public void Save(GatherSession session)
    {
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(session, Options);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    public void Clear() => File.Delete(path);
}
