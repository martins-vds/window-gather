namespace WindowGather;

public sealed record GatherPreferences(bool MakeDestinationPrimary = false, bool FollowNewWindows = false)
{
    public void Validate()
    {
        if (FollowNewWindows && !MakeDestinationPrimary)
            throw new ArgumentException("Following new windows requires making the gathering destination primary.");
    }
}

public interface IGatherPreferencesStore
{
    GatherPreferences Load();
    void Save(GatherPreferences preferences);
}

public interface IPrimaryDisplay
{
    string GetPrimaryDisplayId();
    void SetPrimaryDisplay(string displayId);
}

public interface IWindowTracker
{
    event Action<SavedWindow>? WindowOpened;
    event Action<string>? Problem;
    void Start(string token, Display target);
    void Stop();
}

public interface IWindowInspector
{
    SavedWindow Inspect(SavedWindow window, string token);
}

public enum PrimaryPhase { Prepared, Applied, Released, Restored }

public sealed record PrimaryRecovery(string OriginalId, PrimaryPhase Phase, bool FollowNewWindows)
{
    public bool TrackingInterrupted { get; init; }
    public List<SavedWindow> Followers { get; init; } = [];
    public string? FollowTargetId { get; init; }
    public bool RestoreStarted { get; init; }
}
