namespace WindowGather;

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
