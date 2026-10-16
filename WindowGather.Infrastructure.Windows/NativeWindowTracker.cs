using System.Diagnostics;

namespace WindowGather;

/// <summary>Observes new window lifecycles on the dispatcher's existing message-pump thread.</summary>
public sealed class NativeWindowTracker : IWindowTracker, IDisposable
{
    private const uint ObjectCreate = 0x8000;
    private const uint ObjectDestroy = 0x8001;
    private const uint ObjectShow = 0x8002;
    private const int MaximumHandles = 65536;
    private const int MaximumCandidates = 4096;
    private const long CandidateLifetimeMilliseconds = 120000;

    private readonly IWindowLifecycleNative native;
    private readonly Func<nint, string, Display, SavedWindow?> capture;
    private readonly Action<SavedWindow, string> unmark;
    private readonly Func<Action, Task> dispatch;
    private readonly List<nint> hooks = [];
    private readonly HashSet<nint> known = [];
    private readonly Dictionary<nint, Candidate> candidates = [];
    private uint ownerThread;
    private uint armedAt;
    private long generation;
    private long nextCleanup;
    private string? token;
    private Display? target;
    private bool running;
    private int disposed;

    public event Action<SavedWindow>? WindowOpened;
    public event Action<string>? Problem;

    public NativeWindowTracker(NativeDesktop desktop, Func<Action, Task> dispatch)
        : this(new WindowLifecycleNative(), desktop.CaptureFollower, desktop.Unmark, dispatch) { }

    internal NativeWindowTracker(IWindowLifecycleNative native,
        Func<nint, string, Display, SavedWindow?> capture, Action<SavedWindow, string> unmark,
        Func<Action, Task> dispatch)
    {
        this.native = native;
        this.capture = capture;
        this.unmark = unmark;
        this.dispatch = dispatch;
    }

    public void Start(string token, Display target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        Invoke(() =>
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            StartOnOwner(token, target);
        });
    }

    public void Stop() => Invoke(StopOnOwner);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) Stop();
    }

    private void Invoke(Action action)
    {
        try
        {
            // The supplied dispatcher must execute inline when already on its owner thread.
            dispatch(() =>
            {
                uint current = native.CurrentThreadId;
                if (ownerThread != 0 && ownerThread != current)
                    throw new InvalidOperationException("Window tracking must use one UI message-pump thread.");
                ownerThread = current;
                action();
            }).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Report("Window tracking could not complete its UI-thread operation.", error);
            throw;
        }
    }

    private void StartOnOwner(string sessionToken, Display destination)
    {
        StopOnOwner();
        try
        {
            // Install while disarmed, then baseline even hidden windows. Queued pre-arm
            // events are rejected by their event timestamp, not by delivery time.
            WindowLifecycleCallback handler = OnWinEvent;
            foreach (uint eventId in new[] { ObjectCreate, ObjectDestroy, ObjectShow })
            {
                nint hook = native.SetHook(eventId, handler);
                hooks.Add(hook);
            }

            Exception? baselineError = null;
            Func<nint, bool> enumerate = handle =>
            {
                try
                {
                    if (known.Count >= MaximumHandles)
                        throw new InvalidOperationException("Too many windows to establish a safe tracking baseline.");
                    known.Add(handle);
                    return true;
                }
                catch (Exception error)
                {
                    baselineError = error;
                    return false;
                }
            };
            native.EnumerateWindows(enumerate);
            if (baselineError is not null) throw baselineError;

            token = sessionToken;
            target = destination;
            armedAt = unchecked((uint)native.TickCount64);
            nextCleanup = native.TickCount64 + 1000;
            running = true;
        }
        catch
        {
            StopOnOwner();
            throw;
        }
    }

    private void StopOnOwner()
    {
        running = false;
        generation++;
        token = null;
        target = null;
        candidates.Clear();
        known.Clear();
        Exception? failure = null;
        for (int index = hooks.Count - 1; index >= 0; index--)
        {
            nint hook = hooks[index];
            try { native.RemoveHook(hook); }
            catch (Exception error)
            {
                failure ??= error;
                continue;
            }
            hooks.RemoveAt(index);
        }
        // Session markers belong to the engine once an event is delivered, not to tracking.
        if (failure is not null) throw failure;
    }

    private void OnWinEvent(nint hook, uint eventId, nint handle, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        try
        {
            if (!running || !hooks.Contains(hook) || handle == 0 || objectId != 0 || childId != 0)
                return;
            // Reject the arming millisecond too: it may contain queued baseline events.
            if (unchecked((int)(eventTime - armedAt)) <= 0) return;
            if (native.CurrentThreadId != ownerThread)
                throw new InvalidOperationException("A window lifecycle callback arrived on the wrong thread.");

            if (eventId == ObjectDestroy)
            {
                candidates.Remove(handle);
                // A valid handle here may already belong to a replacement lifetime.
                // Keep it excluded rather than interpreting a delayed DESTROY as proof
                // that a subsequently delivered CREATE describes that replacement.
                if (!native.IsWindow(handle)) known.Remove(handle);
                return;
            }

            CleanupCandidates();
            if (eventId == ObjectCreate)
            {
                if (known.Contains(handle) || !native.IsWindow(handle) || native.GetRoot(handle) != handle)
                    return;
                if (known.Count >= MaximumHandles || candidates.Count >= MaximumCandidates)
                    throw new InvalidOperationException("Window tracking capacity was exceeded; following was stopped.");
                known.Add(handle);
                uint thread = native.GetThreadProcessId(handle, out uint process);
                if (thread == 0 || thread != eventThread || process == 0 || process == native.CurrentProcessId)
                    return;
                candidates.Add(handle, new(process, thread, eventTime, native.TickCount64));
                return;
            }

            if (eventId != ObjectShow || !candidates.Remove(handle, out Candidate? candidate) || candidate is null)
                return;
            // Consume only once. A later SHOW after moving an old window onto the target
            // must not turn that existing window into a new follower.
            if (unchecked((int)(eventTime - candidate.CreatedAt)) < 0 ||
                native.TickCount64 - candidate.ObservedAt > CandidateLifetimeMilliseconds ||
                native.GetRoot(handle) != handle)
                return;
            uint liveThread = native.GetThreadProcessId(handle, out uint liveProcess);
            if (liveThread != candidate.Thread || liveThread != eventThread || liveProcess != candidate.Process)
                return;
            Enroll(handle, candidate);
        }
        catch (Exception error)
        {
            // Never unwind through user32. Unexpected callback failure ends this epoch
            // instead of guessing about partially observed lifecycles.
            try { StopOnOwner(); }
            catch (Exception cleanupError) { TraceFailure(cleanupError); }
            Report("Following new windows stopped because lifecycle tracking failed.", error);
        }
    }

    private void Enroll(nint handle, Candidate candidate)
    {
        string sessionToken = token!;
        Display destination = target!;
        long epoch = generation;
        SavedWindow? window;
        try { window = capture(handle, sessionToken, destination); }
        catch (Exception error)
        {
            Report("A newly opened window could not be enrolled safely.", error);
            return;
        }
        if (window is null) return;

        bool delivered = false;
        try
        {
            Action<SavedWindow>? subscriber = WindowOpened;
            uint liveThread = native.GetThreadProcessId(handle, out uint liveProcess);
            if (!running || generation != epoch || subscriber is null ||
                window.ProcessId != candidate.Process || liveProcess != candidate.Process ||
                liveThread != candidate.Thread)
                return;

            // Ownership transfers at invocation, even if a subscriber throws or starts
            // asynchronous persistence. Stop must never remove a delivered marker.
            delivered = true;
            try { subscriber(window); }
            catch (Exception error) { Report("A new window enrollment could not be delivered.", error); }
        }
        finally
        {
            // Also release an unaccepted marker if validation itself fails.
            if (!delivered) unmark(window, sessionToken);
        }
    }

    private void CleanupCandidates()
    {
        long now = native.TickCount64;
        if (now < nextCleanup) return;
        nextCleanup = now + 1000;
        foreach ((nint handle, Candidate candidate) in candidates.ToArray())
        {
            if (now - candidate.ObservedAt > CandidateLifetimeMilliseconds || !native.IsWindow(handle))
                candidates.Remove(handle);
        }
        // known intentionally retains expired candidates until DESTROY. Eviction must
        // never make a duplicate CREATE sufficient to enroll an existing window.
    }

    private void Report(string message, Exception error)
    {
        TraceFailure(error);
        try { Problem?.Invoke($"{message} {error.Message}"); }
        catch (Exception subscriberError) { TraceFailure(subscriberError); }
    }

    private static void TraceFailure(Exception error)
    {
        try { Trace.TraceError("NativeWindowTracker: {0}", error); }
        catch (Exception) { /* A failing trace listener must not escape a native callback. */ }
    }

    private sealed record Candidate(uint Process, uint Thread, uint CreatedAt, long ObservedAt);

}
