using System.Text.Json.Serialization;

namespace WindowGather;

public readonly record struct Box(int Left, int Top, int Right, int Bottom)
{
    [JsonIgnore] public int Width => Right - Left;
    [JsonIgnore] public int Height => Bottom - Top;
}

public readonly record struct Position(int X, int Y);
public sealed record Placement(uint Flags, uint ShowCommand, Position Min, Position Max, Box Normal);

public sealed record Display(string Id, string DeviceName, string Name, Box Bounds, Box WorkArea)
{
    public override string ToString() =>
        $"{DeviceName.Replace(@"\\.\", "")} - {Name} ({Bounds.Width} x {Bounds.Height})";
}

public sealed record SavedWindow(long Handle, int ProcessId, long ProcessStartedUtcTicks,
    string ClassName, Placement Placement, Display Origin);
public sealed record WindowScan(List<SavedWindow> Windows, List<string> Warnings);

public sealed class GatherSession
{
    public int Version { get; init; } = 1;
    public required string Token { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public required Display Target { get; init; }
    public required List<SavedWindow> Windows { get; set; }
}

public sealed record OperationResult(int Completed, int Skipped, List<string> Problems, string Summary);

public interface IDesktop
{
    IReadOnlyList<Display> GetDisplays();
    Display GetPointerDisplay();
    WindowScan CaptureWindows(string token, Display target);
    bool Matches(SavedWindow window, string token);
    void Mark(SavedWindow window, string token);
    void Unmark(SavedWindow window, string token);
    void Gather(SavedWindow window, Display target);
    void Restore(SavedWindow window);
}

public interface ISessionStore
{
    GatherSession? Load();
    void Save(GatherSession session);
    void Clear();
}

public static class Geometry
{
    public static Box FitNormal(Placement placement, Display origin, Display target)
    {
        Box normal = placement.Normal;
        // WINDOWPLACEMENT uses workspace coordinates; window-moving APIs use screen coordinates.
        int screenX = normal.Left + origin.WorkArea.Left - origin.Bounds.Left;
        int screenY = normal.Top + origin.WorkArea.Top - origin.Bounds.Top;
        double xFraction = (double)(screenX - origin.WorkArea.Left) / origin.WorkArea.Width;
        double yFraction = (double)(screenY - origin.WorkArea.Top) / origin.WorkArea.Height;
        int width = Math.Clamp(normal.Width, 1, target.WorkArea.Width);
        int height = Math.Clamp(normal.Height, 1, target.WorkArea.Height);
        int x = target.WorkArea.Left + (int)Math.Round(xFraction * target.WorkArea.Width);
        int y = target.WorkArea.Top + (int)Math.Round(yFraction * target.WorkArea.Height);
        x = Math.Clamp(x, target.WorkArea.Left, target.WorkArea.Right - width);
        y = Math.Clamp(y, target.WorkArea.Top, target.WorkArea.Bottom - height);
        x -= target.WorkArea.Left - target.Bounds.Left;
        y -= target.WorkArea.Top - target.Bounds.Top;
        return new Box(x, y, x + width, y + height);
    }

    public static int State(uint command) => command switch
    {
        2 or 6 or 7 or 11 => 2,
        3 => 3,
        _ => 1
    };
}
