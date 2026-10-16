using System.ComponentModel;

namespace WindowGather;

public sealed class GatherEngine
{
    private readonly IDesktop desktop;
    private readonly ISessionStore store;
    private bool topologyObserved;
    private string? automaticReturnToken;
    private readonly IPrimaryDisplay? primaryDisplay;
    private readonly IWindowInspector? inspector;
    private readonly IWindowTracker? tracker;
    private readonly IGatherPreferencesStore? preferencesStore;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Token, string Id, bool DestinationPresent)> primaryObservations = new();
    private int changingPrimary;
    public GatherPreferences Preferences { get; private set; } = new();
    public string PreferencesWarning { get; private set; } = "";
    private string trackingProblem = "";
    private bool trackingStopped;
    private string? releasedPrimaryToken;
    private string? uncertainPrimaryToken;
    public bool IsChangingPrimary => Volatile.Read(ref changingPrimary) != 0;
    public GatherSession? Session { get; private set; }

    public GatherEngine(IDesktop desktop, ISessionStore store, IPrimaryDisplay? primaryDisplay = null,
        IWindowInspector? inspector = null, IWindowTracker? tracker = null,
        IGatherPreferencesStore? preferencesStore = null)
    {
        this.desktop = desktop;
        this.store = store;
        this.primaryDisplay = primaryDisplay;
        this.inspector = inspector;
        this.tracker = tracker;
        this.preferencesStore = preferencesStore;
        Session = store.Load();
        if (Session?.Primary is { } recovery)
            Session.Primary = recovery with
            {
                Phase = recovery.Phase == PrimaryPhase.Applied ? PrimaryPhase.Prepared : recovery.Phase,
                TrackingInterrupted = recovery.FollowNewWindows
            };
        if (preferencesStore is not null)
        {
            try { Preferences = preferencesStore.Load(); Preferences.Validate(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            {
                Preferences = new();
                PreferencesWarning = "Cannot load gathering options: " + error.Message + " Options are off until repaired and saved.";
            }
        }
    }

    public string? GetPrimaryDisplayId() => primaryDisplay?.GetPrimaryDisplayId();

    public void SavePreferences(GatherPreferences preferences)
    {
        if (Session is not null) throw new InvalidOperationException("Finish or forget recovery before changing gathering options.");
        preferences.Validate();
        if (preferences.MakeDestinationPrimary && (primaryDisplay is null || inspector is null ||
            (preferences.FollowNewWindows && tracker is null)))
            throw new InvalidOperationException("Primary-display options are unavailable in this frontend.");
        if (preferencesStore is null) throw new InvalidOperationException("Gathering options storage is unavailable.");
        preferencesStore.Save(preferences);
        Preferences = preferences;
        PreferencesWarning = "";
    }

    public void ObservePrimary()
    {
        GatherSession? session = Session;
        if (session?.Primary is null || IsChangingPrimary || primaryDisplay is null) return;
        try
        {
            string current = primaryDisplay.GetPrimaryDisplayId();
            bool destinationPresent = GetDisplays().Any(d => d.Id == session.Target.Id);
            primaryObservations.Enqueue((session.Token, current, destinationPresent));
        }
        catch
        {
            primaryObservations.Enqueue((session.Token, "", false));
            throw;
        }
    }

    private void ApplyPrimaryObservations()
    {
        var observed = DrainPrimaryObservations();
        if (observed.Released) releasedPrimaryToken = Session?.Token;
        if (observed.Uncertain) uncertainPrimaryToken = Session?.Token;
        if (Session is not { Primary.Phase: PrimaryPhase.Applied } session) return;
        if (releasedPrimaryToken == session.Token)
            SavePrimary(session, session.Primary with { Phase = PrimaryPhase.Released });
        else if (uncertainPrimaryToken == session.Token)
            SavePrimary(session, session.Primary with { Phase = PrimaryPhase.Prepared });
    }

    private (bool Released, bool Uncertain) DrainPrimaryObservations()
    {
        bool released = false;
        bool uncertain = false;
        while (primaryObservations.TryDequeue(out var observed))
        {
            if (Session?.Token != observed.Token) continue;
            uncertain |= observed.Id.Length == 0;
            if (Session is { Primary.Phase: PrimaryPhase.Applied } session &&
                observed.DestinationPresent && observed.Id != session.Target.Id)
                released = true;
        }
        return (released, uncertain);
    }

    private void SavePrimary(GatherSession session, PrimaryRecovery recovery)
    {
        GatherSession updated = CopySession(session, session.Windows, recovery);
        store.Save(updated);
        Session = updated;
    }

    private static GatherSession CopySession(GatherSession session, List<SavedWindow> windows, PrimaryRecovery? primary) => new()
    {
        Version = session.Version, Token = session.Token, CreatedUtc = session.CreatedUtc,
        Target = session.Target, Windows = windows, AutomaticReturnAttempted = session.AutomaticReturnAttempted,
        Primary = primary
    };

    public OperationResult TrackWindow(SavedWindow window, string token)
    {
        OperationResult quiet = new(0, 0, [], "") { Announce = false };
        if (Session is not { Primary: { FollowNewWindows: true, RestoreStarted: false } recovery } session ||
            session.Token != token ||
            recovery.TrackingInterrupted || trackingStopped || window.Origin.Id != session.Target.Id ||
            RecordedWindow(session, window.Handle))
        {
            if (Session?.Token != token || !RecordedWindow(Session, window.Handle))
                desktop.Unmark(window, token);
            return quiet;
        }

        if (recovery.Followers.Count >= 10000)
        {
            desktop.Unmark(window, token);
            return ReportTrackingProblem("The safe limit of 10,000 recorded new windows was reached.");
        }
        try { SavePrimary(session, recovery with { Followers = [.. recovery.Followers, window] }); }
        catch
        {
            desktop.Unmark(window, session.Token);
            trackingProblem = "New-window tracking could not be saved. Following is paused; previously saved recovery remains available.";
            trackingStopped = true;
            tracker?.Stop();
            throw;
        }
        return quiet;
    }

    private static bool RecordedWindow(GatherSession? session, long handle) => session is not null &&
        (session.Windows.Any(w => w.Handle == handle) || session.Primary?.Followers.Any(w => w.Handle == handle) == true);

    public OperationResult ReportTrackingProblem(string problem)
    {
        trackingProblem = "New-window tracking stopped: " + problem;
        trackingStopped = true;
        tracker?.Stop();
        if (Session is { Primary: { } recovery } session)
            SavePrimary(session, recovery with { TrackingInterrupted = true });
        return new(0, 0, [trackingProblem], "New-window tracking needs attention.");
    }

    public IReadOnlyList<Display> GetDisplays()
    {
        IReadOnlyList<Display> displays = desktop.GetDisplays();
        if (displays.Any(d => string.IsNullOrWhiteSpace(d.Id) || d.Bounds.Width <= 0 ||
            d.Bounds.Height <= 0 || d.WorkArea.Width <= 0 || d.WorkArea.Height <= 0) ||
            displays.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != displays.Count)
            throw new InvalidOperationException("The display inventory is invalid or ambiguous. Retry after the layout settles.");
        if (primaryDisplay is null) return displays;
        string primary = primaryDisplay.GetPrimaryDisplayId();
        return displays.Select(d => d with { IsPrimary = d.Id == primary }).ToArray();
    }
    public Display GetPointerDisplay() => desktop.GetPointerDisplay();

    public OperationResult Gather(Display target)
    {
        if (Session is not null)
            throw new InvalidOperationException("Restore the current session before gathering again.");
        trackingStopped = false;
        trackingProblem = "";
        if (!GetDisplays().Any(d => d.Id == target.Id && d.Bounds == target.Bounds &&
            d.WorkArea == target.WorkArea))
            throw new InvalidOperationException("The selected display changed. Refresh the display list.");
        Preferences.Validate();
        EnsurePrimaryCapabilities();
        string? originalPrimary = Preferences.MakeDestinationPrimary ? primaryDisplay!.GetPrimaryDisplayId() : null;
        string token = Guid.NewGuid().ToString("D");
        WindowScan scan = desktop.CaptureWindows(token, target);
        List<SavedWindow> imported = scan.Windows.Where(w => w.Origin.Id != target.Id).ToList();
        if (imported.Count == 0 && !Preferences.MakeDestinationPrimary)
            return new(0, 0, scan.Warnings, "No movable windows were found on the other displays.");
        var session = new GatherSession
        {
            Token = token, Target = target, Windows = imported,
            Primary = Preferences.MakeDestinationPrimary
                ? new(originalPrimary!, PrimaryPhase.Prepared, Preferences.FollowNewWindows) : null
        };
        try { store.Save(session); }
        catch
        {
            foreach (SavedWindow window in imported) desktop.Unmark(window, token);
            throw;
        }
        Session = session;
        topologyObserved = true;
        target = BeginPrimaryGather(session, target);
        session = Session!;
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
        if (session.Primary is { FollowNewWindows: true } trackingRecovery)
        {
            try { tracker!.Start(session.Token, target); }
            catch (Exception error) when (IsWindowError(error))
            {
                SavePrimary(session, trackingRecovery with { TrackingInterrupted = true });
                problems.Add("New-window tracking could not start: " + error.Message);
            }
        }
        return new(completed, 0, problems,
            $"Gathered {completed} of {imported.Count} windows. Windows already on the target were left untouched.");
    }

    private void EnsurePrimaryCapabilities()
    {
        if (Preferences.MakeDestinationPrimary && (primaryDisplay is null || inspector is null ||
            (Preferences.FollowNewWindows && tracker is null)))
            throw new InvalidOperationException("This frontend cannot safely provide the selected primary-display options.");
    }

    private Display BeginPrimaryGather(GatherSession session, Display target)
    {
        if (session.Primary is not { } primary) return target;
        Interlocked.Exchange(ref changingPrimary, 1);
        try
        {
            if (primaryDisplay!.GetPrimaryDisplayId() != primary.OriginalId)
                throw new InvalidOperationException("The primary display changed before Gather. Restore this recovery, then retry.");
            if (primary.OriginalId != target.Id) primaryDisplay!.SetPrimaryDisplay(target.Id);
            SavePrimary(session, primary with { Phase = PrimaryPhase.Applied });
            return GetDisplays().Single(d => d.Id == target.Id);
        }
        finally { Interlocked.Exchange(ref changingPrimary, 0); }
    }

    public OperationResult ReturnAfterDestinationLoss(IReadOnlyList<Display> displays)
    {
        ApplyPrimaryObservations();
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
            AutomaticReturnAttempted = true, Primary = session.Primary
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
        if (session.Primary is { } primary)
            return $"Recovery includes {session.Windows.Count} borrowed windows, {primary.Followers.Count} new windows and primary-display state. " +
                "Use Restore to finish; Forget also abandons primary recovery." +
                (primary.TrackingInterrupted ? " New-window tracking was interrupted; only previously recorded windows can follow." : "") +
                (trackingProblem.Length > 0 ? " " + trackingProblem : "");
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
        ApplyPrimaryObservations();
        GatherSession session = Session ??
            throw new InvalidOperationException("There is no active gather session.");
        if (session.Primary is not null && primaryDisplay is null)
            throw new InvalidOperationException("Open the WinUI frontend to restore this primary-display session. Recovery has not been changed.");
        _ = GetDisplays();
        var remaining = new List<SavedWindow>();
        var problems = new List<string>();
        int completed = 0;
        int skipped = 0;
        session = PrepareRestore(session, automaticOrigins is not null, problems);
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
        return CompleteRestore(session, remaining, automaticOrigins is not null, problems, completed, skipped);
    }

    private GatherSession PrepareRestore(GatherSession session, bool automatic, List<string> problems)
    {
        if (automatic || session.Primary is not { } recovery) return session;
        tracker?.Stop();
        SavePrimary(session, recovery with { RestoreStarted = true });
        try { RestorePrimary(Session!); }
        catch (Exception error) when (IsWindowError(error))
        {
            problems.Add("Primary display: " + error.Message);
        }
        return Session!;
    }

    private OperationResult CompleteRestore(GatherSession session, List<SavedWindow> remaining,
        bool automatic, List<string> problems, int completed, int skipped)
    {
        PrimaryRecovery? primaryRemaining = session.Primary;
        if (!automatic && primaryRemaining is { Phase: PrimaryPhase.Restored or PrimaryPhase.Released })
        {
            primaryRemaining = RestoreFollowers(session, primaryRemaining, problems, ref completed, ref skipped);
            session = Session!;
        }
        bool complete = remaining.Count == 0 && PrimaryComplete(primaryRemaining, automatic);
        // Save completion before removing markers, so a failed disk write remains safely retryable.
        if (complete) store.Clear();
        else store.Save(CopySession(session, remaining, primaryRemaining));
        List<SavedWindow> resolved = session.Windows.Except(remaining).ToList();
        Session = complete ? null : CopySession(session, remaining, primaryRemaining);
        foreach (SavedWindow window in resolved) desktop.Unmark(window, session.Token);
        return new(completed, skipped, problems,
            $"Restored {completed} windows; skipped {skipped} closed or unmarked windows. " +
            (complete ? "The session is complete." :
                session.Primary is null ? $"{remaining.Count} still need restoration." : "Recovery is still pending."));
    }

    private static bool PrimaryComplete(PrimaryRecovery? primary, bool automatic) =>
        primary is null || (!automatic && primary.Phase is PrimaryPhase.Restored or PrimaryPhase.Released &&
            primary.Followers.Count == 0);

    private void RestorePrimary(GatherSession session)
    {
        PrimaryRecovery recovery = session.Primary!;
        if (recovery.Phase is PrimaryPhase.Restored or PrimaryPhase.Released) return;
        string current = primaryDisplay!.GetPrimaryDisplayId();
        if (current == recovery.OriginalId)
        {
            SavePrimary(session, recovery with { Phase = PrimaryPhase.Restored });
            return;
        }
        if (current != session.Target.Id && GetDisplays().Any(d => d.Id == session.Target.Id))
        {
            SavePrimary(session, recovery with { Phase = PrimaryPhase.Released });
            return;
        }
        if (recovery.Phase == PrimaryPhase.Prepared)
            throw new InvalidOperationException("A primary change was interrupted. Set the intended primary in Windows display settings, then retry Restore. Automatic reversal cannot safely verify ownership.");
        if (!GetDisplays().Any(d => d.Id == recovery.OriginalId))
            throw new InvalidOperationException("Reconnect the original primary display, then retry Restore.");
        Interlocked.Exchange(ref changingPrimary, 1);
        try
        {
            primaryDisplay.SetPrimaryDisplay(recovery.OriginalId);
            SavePrimary(session, recovery with { Phase = PrimaryPhase.Restored });
        }
        finally { Interlocked.Exchange(ref changingPrimary, 0); }
    }

    private PrimaryRecovery RestoreFollowers(GatherSession session, PrimaryRecovery recovery,
        List<string> problems, ref int completed, ref int skipped)
    {
        if (recovery.Followers.Count == 0) return recovery;
        if (inspector is null) throw new InvalidOperationException("New-window inspection is unavailable.");
        string targetId = recovery.FollowTargetId ?? primaryDisplay!.GetPrimaryDisplayId();
        recovery = recovery with { FollowTargetId = targetId };
        SavePrimary(session, recovery);
        session = Session!;
        var remaining = new List<SavedWindow>();
        var resolved = new List<SavedWindow>();
        foreach (SavedWindow window in recovery.Followers)
        {
            try
            {
                if (!desktop.Matches(window, session.Token)) { skipped++; resolved.Add(window); continue; }
                SavedWindow current = inspector.Inspect(window, session.Token);
                if (current.Origin.Id != session.Target.Id) { skipped++; resolved.Add(window); continue; }
                if (primaryDisplay!.GetPrimaryDisplayId() != targetId)
                    throw new InvalidOperationException("The primary changed during Restore. Restore the intended primary before retrying.");
                Display target = GetDisplays().SingleOrDefault(d => d.Id == targetId)
                    ?? throw new InvalidOperationException("The follow destination is unavailable.");
                recovery = recovery with
                {
                    Followers = recovery.Followers.Select(w => w.Handle == current.Handle ? current : w).ToList()
                };
                SavePrimary(session, recovery);
                session = Session!;
                if (target.Id != current.Origin.Id) desktop.Gather(current, target);
                completed++;
                resolved.Add(window);
            }
            catch (Exception error) when (IsWindowError(error))
            {
                problems.Add($"New window {window.Handle}: {error.Message}");
                remaining.Add(window);
            }
        }
        recovery = recovery with { Followers = remaining };
        SavePrimary(session, recovery);
        foreach (SavedWindow window in resolved) desktop.Unmark(window, session.Token);
        return recovery;
    }

    public void Forget()
    {
        if (Session is null) return;
        GatherSession session = Session;
        tracker?.Stop();
        store.Clear();
        Session = null;
        foreach (SavedWindow window in session.Windows) desktop.Unmark(window, session.Token);
        if (session.Primary is { } recovery)
            foreach (SavedWindow window in recovery.Followers) desktop.Unmark(window, session.Token);
    }

    private static bool IsWindowError(Exception error) =>
        error is Win32Exception or InvalidOperationException or ArgumentException;
}
