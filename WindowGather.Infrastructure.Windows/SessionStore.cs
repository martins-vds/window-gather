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
        var data = JsonSerializer.Deserialize<SessionData>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"The recovery file is empty: {path}");
        if (data.Version != 1 || !Guid.TryParse(data.Token, out _) ||
            data.Windows is null || data.Windows.Count > 10000 ||
            data.Target is null || string.IsNullOrWhiteSpace(data.Target.Id) ||
            data.Windows.Any(w => w is null || w.Handle == 0 || w.ProcessId <= 0 ||
                w.ProcessStartedUtcTicks <= 0 || string.IsNullOrWhiteSpace(w.ClassName) ||
                w.Placement is null || w.Placement.Normal.Width <= 0 ||
                w.Placement.Normal.Height <= 0 || w.Origin is null ||
                string.IsNullOrWhiteSpace(w.Origin.Id) || w.Origin.WorkArea.Width <= 0 ||
                w.Origin.WorkArea.Height <= 0 || w.Origin.Id == data.Target.Id))
            throw new InvalidDataException($"The recovery file has invalid data: {path}");
        return data.ToSession();
    }

    public void Save(GatherSession session)
    {
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(SessionData.From(session), Options);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    public void Clear() => File.Delete(path);

    private readonly record struct BoxData(int Left, int Top, int Right, int Bottom)
    {
        [System.Text.Json.Serialization.JsonIgnore] public int Width => Right - Left;
        [System.Text.Json.Serialization.JsonIgnore] public int Height => Bottom - Top;
        public Box ToBox() => new(Left, Top, Right, Bottom);
        public static BoxData From(Box box) => new(box.Left, box.Top, box.Right, box.Bottom);
    }

    private sealed record DisplayData(string Id, string DeviceName, string Name, BoxData Bounds, BoxData WorkArea)
    {
        public Display ToDisplay() => new(Id, DeviceName, Name, Bounds.ToBox(), WorkArea.ToBox());
        public static DisplayData From(Display display) =>
            new(display.Id, display.DeviceName, display.Name, BoxData.From(display.Bounds), BoxData.From(display.WorkArea));
    }

    private sealed record PlacementData(uint Flags, uint ShowCommand, Position Min, Position Max, BoxData Normal)
    {
        public Placement ToPlacement() => new(Flags, ShowCommand, Min, Max, Normal.ToBox());
        public static PlacementData From(Placement placement) =>
            new(placement.Flags, placement.ShowCommand, placement.Min, placement.Max, BoxData.From(placement.Normal));
    }

    private sealed record WindowData(long Handle, int ProcessId, long ProcessStartedUtcTicks,
        string ClassName, PlacementData Placement, DisplayData Origin)
    {
        public SavedWindow ToWindow() => new(Handle, ProcessId, ProcessStartedUtcTicks,
            ClassName, Placement.ToPlacement(), Origin.ToDisplay());
        public static WindowData From(SavedWindow window) => new(window.Handle, window.ProcessId,
            window.ProcessStartedUtcTicks, window.ClassName, PlacementData.From(window.Placement), DisplayData.From(window.Origin));
    }

    private sealed record SessionData(int Version, string Token, DateTime CreatedUtc, DisplayData Target, List<WindowData> Windows)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool AutomaticReturnAttempted { get; init; }

        public GatherSession ToSession() => new()
        {
            Version = Version, Token = Token, CreatedUtc = CreatedUtc, Target = Target.ToDisplay(),
            Windows = Windows.Select(w => w.ToWindow()).ToList(),
            AutomaticReturnAttempted = AutomaticReturnAttempted
        };
        public static SessionData From(GatherSession session) => new(session.Version, session.Token,
            session.CreatedUtc, DisplayData.From(session.Target), session.Windows.Select(WindowData.From).ToList())
            { AutomaticReturnAttempted = session.AutomaticReturnAttempted };
    }
}
