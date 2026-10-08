using System.ComponentModel;

namespace WindowGather;

public sealed class GatherEngine
{
    private readonly IDesktop desktop;
    private readonly ISessionStore store;
    public GatherSession? Session { get; private set; }

    public GatherEngine(IDesktop desktop, ISessionStore store)
    {
        this.desktop = desktop;
        this.store = store;
        Session = store.Load();
    }

    public IReadOnlyList<Display> GetDisplays() => desktop.GetDisplays();
    public Display GetPointerDisplay() => desktop.GetPointerDisplay();

    public OperationResult Gather(Display target)
    {
        if (Session is not null)
            throw new InvalidOperationException("Restore the current session before gathering again.");
        if (!desktop.GetDisplays().Any(d => d.Id == target.Id && d.Bounds == target.Bounds &&
            d.WorkArea == target.WorkArea))
            throw new InvalidOperationException("The selected display changed. Refresh the display list.");
        string token = Guid.NewGuid().ToString("D");
        WindowScan scan = desktop.CaptureWindows(token, target);
        List<SavedWindow> imported = scan.Windows.Where(w => w.Origin.Id != target.Id).ToList();
        if (imported.Count == 0)
            return new(0, 0, scan.Warnings, "No movable windows were found on the other displays.");
        var session = new GatherSession
        {
            Token = token, Target = target, Windows = imported
        };
        try { store.Save(session); }
        catch
        {
            foreach (SavedWindow window in imported) desktop.Unmark(window, token);
            throw;
        }
        Session = session;
        int completed = 0;
        var problems = new List<string>(scan.Warnings);
        foreach (SavedWindow window in imported)
        {
            try
            {
                desktop.Mark(window, session.Token);
                desktop.Gather(window, target);
                completed++;
            }
            catch (Exception error) when (IsWindowError(error))
            {
                problems.Add($"Window {window.Handle}: {error.Message}");
            }
        }
        return new(completed, 0, problems,
            $"Gathered {completed} of {imported.Count} windows. Windows already on the target were left untouched.");
    }

    public OperationResult Restore()
    {
        GatherSession session = Session ??
            throw new InvalidOperationException("There is no active gather session.");
        IReadOnlyList<Display> displays = desktop.GetDisplays();
        var remaining = new List<SavedWindow>();
        var problems = new List<string>();
        int completed = 0;
        int skipped = 0;
        foreach (SavedWindow window in session.Windows)
        {
            try
            {
                if (!desktop.Matches(window, session.Token)) { skipped++; continue; }
                Display? origin = displays.FirstOrDefault(d => d.Id == window.Origin.Id);
                if (origin is null)
                    throw new InvalidOperationException(
                        $"Reconnect {window.Origin.DeviceName}, then click Restore again.");
                if (origin.Bounds != window.Origin.Bounds || origin.WorkArea != window.Origin.WorkArea)
                    throw new InvalidOperationException(
                        $"The layout of {window.Origin.DeviceName} changed. Restore its original " +
                        "position, resolution, and taskbar arrangement, then retry.");
                desktop.Restore(window);
                completed++;
            }
            catch (Exception error) when (IsWindowError(error))
            {
                problems.Add($"Window {window.Handle}: {error.Message}");
                remaining.Add(window);
            }
        }
        // Save completion before removing markers, so a failed disk write remains safely retryable.
        if (remaining.Count == 0) store.Clear();
        else store.Save(new GatherSession
        {
            Token = session.Token, Target = session.Target,
            CreatedUtc = session.CreatedUtc, Windows = remaining
        });
        List<SavedWindow> resolved = session.Windows.Except(remaining).ToList();
        session.Windows = remaining;
        Session = remaining.Count == 0 ? null : session;
        foreach (SavedWindow window in resolved) desktop.Unmark(window, session.Token);
        return new(completed, skipped, problems,
            $"Restored {completed} windows; skipped {skipped} closed or unmarked windows. " +
            (remaining.Count == 0 ? "The session is complete." : $"{remaining.Count} still need restoration."));
    }

    public void Forget()
    {
        if (Session is null) return;
        GatherSession session = Session;
        store.Clear();
        Session = null;
        foreach (SavedWindow window in session.Windows) desktop.Unmark(window, session.Token);
    }

    private static bool IsWindowError(Exception error) =>
        error is Win32Exception or InvalidOperationException or ArgumentException;
}
