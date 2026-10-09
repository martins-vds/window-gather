using System.ComponentModel;

namespace WindowGather;

public sealed class GatherEngine
{
    private readonly IDesktop desktop;
    private readonly ISessionStore store;
    private bool topologyObserved;
    private string? automaticReturnToken;
    public GatherSession? Session { get; private set; }

    public GatherEngine(IDesktop desktop, ISessionStore store)
    {
        this.desktop = desktop;
        this.store = store;
        Session = store.Load();
    }

    public IReadOnlyList<Display> GetDisplays()
    {
        IReadOnlyList<Display> displays = desktop.GetDisplays();
        if (displays.Any(d => string.IsNullOrWhiteSpace(d.Id) || d.Bounds.Width <= 0 ||
            d.Bounds.Height <= 0 || d.WorkArea.Width <= 0 || d.WorkArea.Height <= 0) ||
            displays.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != displays.Count)
            throw new InvalidOperationException("The display inventory is invalid or ambiguous. Retry after the layout settles.");
        return displays;
    }
    public Display GetPointerDisplay() => desktop.GetPointerDisplay();

    public OperationResult Gather(Display target)
    {
        if (Session is not null)
            throw new InvalidOperationException("Restore the current session before gathering again.");
        if (!GetDisplays().Any(d => d.Id == target.Id && d.Bounds == target.Bounds &&
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
        topologyObserved = true;
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

    public OperationResult ReturnAfterDestinationLoss(IReadOnlyList<Display> displays)
    {
        OperationResult unchanged = new(0, 0, [], "") { Announce = false };
        if (Session is not { AutomaticReturnAttempted: false } session) return unchanged;
        bool destinationPresent = displays.Any(d => d.Id == session.Target.Id);
        if (!topologyObserved)
        {
            topologyObserved = true;
            // Recovery loaded with its destination already absent is pending, not a new disconnect.
            if (!destinationPresent) automaticReturnToken = session.Token;
        }
        if (destinationPresent || automaticReturnToken == session.Token ||
            GetDisplays().Any(d => d.Id == session.Target.Id))
            return unchanged;

        automaticReturnToken = session.Token;
        var attempted = new GatherSession
        {
            Version = session.Version, Token = session.Token, Target = session.Target,
            CreatedUtc = session.CreatedUtc, Windows = session.Windows.ToList(),
            AutomaticReturnAttempted = true
        };
        // Persist the one-shot decision before moving anything; reconnect/restart must not retry it.
        store.Save(attempted);
        Session = attempted;
        OperationResult result = Restore(displays.Select(d => d.Id).ToHashSet(StringComparer.Ordinal));
        return result with { Summary = "Destination disconnected. " + result.Summary };
    }

    public string RecoveryWarning(IReadOnlyList<Display> displays)
    {
        if (Session is not { } session) return "";
        if (session.AutomaticReturnAttempted || automaticReturnToken == session.Token)
            return $"{session.Windows.Count} borrowed windows still need restoration. " +
                "Reconnect unavailable origin displays, then click Restore. Reconnection will not move windows automatically.";
        int unavailable = session.Windows.Count(w => !displays.Any(d => d.Id == w.Origin.Id));
        return unavailable == 0 ? "" :
            $"{unavailable} borrowed windows have an unavailable origin display. " +
            "Recovery is saved; reconnect the display before restoring.";
    }

    public OperationResult Restore() => Restore(null);

    private OperationResult Restore(IReadOnlySet<string>? automaticOrigins)
    {
        GatherSession session = Session ??
            throw new InvalidOperationException("There is no active gather session.");
        _ = GetDisplays();
        var remaining = new List<SavedWindow>();
        var problems = new List<string>();
        int completed = 0;
        int skipped = 0;
        foreach (SavedWindow window in session.Windows)
        {
            try
            {
                if (!desktop.Matches(window, session.Token)) { skipped++; continue; }
                if (automaticOrigins is not null && !automaticOrigins.Contains(window.Origin.Id))
                    throw new InvalidOperationException(
                        $"The origin {window.Origin.DeviceName} was unavailable when the destination disconnected. " +
                        "Reconnect it, then click Restore.");
                IReadOnlyList<Display> displays = GetDisplays();
                Display? origin = displays.FirstOrDefault(d => d.Id == window.Origin.Id);
                if (origin is null)
                    throw new InvalidOperationException(
                        $"Reconnect {window.Origin.DeviceName}, then click Restore again.");
                SavedWindow returning = window;
                if (origin.Bounds != window.Origin.Bounds || origin.WorkArea != window.Origin.WorkArea)
                {
                    returning = window with
                    {
                        Origin = origin,
                        Placement = window.Placement with
                        {
                            Normal = Geometry.FitNormal(window.Placement, window.Origin, origin),
                            Min = new(-1, -1), Max = new(-1, -1)
                        }
                    };
                }
                desktop.Restore(returning);
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
            Version = session.Version, CreatedUtc = session.CreatedUtc, Windows = remaining,
            AutomaticReturnAttempted = session.AutomaticReturnAttempted
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
