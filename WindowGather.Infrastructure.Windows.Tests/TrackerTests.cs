using System.Collections.Concurrent;
using System.Diagnostics;
using WindowGather;

internal static class TrackerTests
{
    private const uint Create = 0x8000;
    private const uint Destroy = 0x8001;
    private const uint Show = 0x8002;
    private static readonly Display Target = new("target", "display", "Target",
        new(0, 0, 1920, 1080), new(0, 0, 1920, 1040));

    public static int Run()
    {
        Action[] tests =
        [
            NewLifecycle, HiddenBaseline, ShowWithoutCreate, DuplicateEvents, DestroyAndReuse,
            ReuseWithoutDestroy, DelayedDestroy, StopAndRestart, FiltersAndIneligible,
            EnrollmentFailure, SubscriberAndReporterFailure, CallbackFailure,
            UnacceptedMarkers, PostCaptureFailure, StopDuringCapture, ExpiredCandidate,
            CandidateCapacity, HookAndBaselineFailure, FailedUnhookCanRetry, OffThreadDispatch,
            WrongThreadDispatch, DispatcherFailure, TraceListenerFailure
        ];
        foreach (Action test in tests)
        {
            try { test(); }
            catch (Exception error) { throw new InvalidOperationException($"Tracker test {test.Method.Name} failed.", error); }
        }
        return tests.Length;
    }

    private static void NewLifecycle()
    {
        using var f = new Fixture();
        f.Start();
        f.Native.Add(10);
        f.Emit(Create, 10);
        Check(f.Opened.Count == 0, "CREATE alone must not enroll.");
        f.Emit(Show, 10);
        Check(f.Opened.Count == 1 && f.Opened[0].Handle == 10, "CREATE then SHOW must enroll.");
        Check(f.CaptureCount == 1 && f.CapturedToken == "session" && f.CapturedTarget == Target,
            "Capture must use the active token and target.");
        f.Tracker.Stop();
        Check(f.Markers.Contains(10), "Stop must preserve asynchronously accepted session markers.");
    }

    private static void HiddenBaseline()
    {
        using var f = new Fixture();
        f.Native.Add(10); // Baseline enumeration deliberately does not consult visibility.
        f.Start();
        f.Emit(Show, 10);
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0 && f.Opened.Count == 0, "Hidden baseline windows are not new lifecycles.");
    }

    private static void ShowWithoutCreate()
    {
        using var f = new Fixture();
        f.Start();
        f.Native.Add(10);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0, "SHOW without an observed CREATE must not guess.");
        f.Emit(Create, 10, time: 1000);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0, "A queued event from the arming millisecond must be ignored.");
    }

    private static void DuplicateEvents()
    {
        using var f = new Fixture();
        f.Start();
        f.Native.Add(10);
        f.Emit(Create, 10);
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        f.Emit(Show, 10);
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        Check(f.Opened.Count == 1 && f.CaptureCount == 1, "A lifetime can be delivered only once.");
    }

    private static void DestroyAndReuse()
    {
        using var f = new Fixture();
        f.Native.Add(10);
        f.Start();
        f.Native.Windows.Remove(10);
        f.Emit(Destroy, 10);
        f.Native.Add(10, process: 300);
        f.Emit(Create, 10);
        f.Native.Windows.Remove(10);
        f.Emit(Destroy, 10);
        f.Native.Add(10, process: 400);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0, "Destruction must discard an unshown candidate.");
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        Check(f.Opened.Count == 1 && f.Opened[0].ProcessId == 400,
            "A replacement requires its own observed CREATE and SHOW.");
    }

    private static void ReuseWithoutDestroy()
    {
        using var f = new Fixture();
        f.Start();
        f.Native.Add(10);
        f.Emit(Create, 10);
        f.Native.Add(10, process: 400);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0, "Identity changes before SHOW must be rejected.");
    }

    private static void DelayedDestroy()
    {
        using var f = new Fixture();
        f.Native.Add(10);
        f.Start();
        f.Native.Add(10, process: 400);
        f.Emit(Destroy, 10);
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0, "A delayed DESTROY for an already reused handle must not permit guessing.");
    }

    private static void StopAndRestart()
    {
        using var f = new Fixture();
        f.Start();
        f.Native.Add(10);
        f.Emit(Create, 10);
        f.Tracker.Stop();
        Check(f.Native.ActiveHooks == 0, "Stop must remove all hooks.");
        f.Emit(Show, 10, includeRemoved: true);
        Check(f.CaptureCount == 0, "Queued callbacks after Stop must be ignored.");
        f.Native.Clock += 100;
        f.Start();
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0, "Restart must take a fresh baseline.");
    }

    private static void FiltersAndIneligible()
    {
        using var f = new Fixture();
        f.Start();
        f.Native.Add(10, process: f.Native.CurrentProcessId);
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        f.Native.Add(11, root: 10);
        f.Emit(Create, 11);
        f.Emit(Show, 11);
        f.Native.Add(12);
        f.Emit(Create, 12, objectId: -4);
        f.Emit(Create, 12, childId: 1);
        f.Emit(Create, 12, eventThread: 99);
        f.Emit(Show, 12);
        Check(f.CaptureCount == 0, "Own process, children, non-window objects and identity races must be excluded.");
        f.Eligible = false;
        f.Native.Add(13);
        f.Emit(Create, 13);
        f.Emit(Show, 13);
        f.Eligible = true;
        f.Emit(Show, 13);
        Check(f.CaptureCount == 1 && f.Opened.Count == 0 && f.Problems.Count == 0,
            "Ineligible/off-target windows are silent and are not retried on a later SHOW.");
    }

    private static void EnrollmentFailure()
    {
        using var f = new Fixture();
        f.Start();
        f.CaptureError = true;
        f.Open(10);
        f.Emit(Show, 10);
        Check(f.Problems.Count == 1 && f.Markers.Count == 0, "Enrollment failure must be explicit, once per lifetime.");
        f.CaptureError = false;
        f.Open(11);
        Check(f.Opened.Count == 1, "One enrollment failure must not disable unrelated new windows.");
    }

    private static void SubscriberAndReporterFailure()
    {
        using var f = new Fixture();
        f.Tracker.WindowOpened += _ => throw new InvalidOperationException("subscriber failure");
        f.Tracker.Problem += _ => throw new InvalidOperationException("reporter failure");
        f.Start();
        f.Open(10);
        Check(f.Problems.Count == 1 && f.Markers.Contains(10), "A delivered marker belongs to the engine even if delivery throws.");
        f.Tracker.Stop();
        Check(f.Markers.Contains(10) && f.UnmarkCount == 0, "Stop must not undo potentially accepted markers.");
    }

    private static void CallbackFailure()
    {
        using var f = new Fixture();
        f.Tracker.Problem += _ => throw new InvalidOperationException("reporter failure");
        f.Start();
        f.Native.Add(10);
        f.Native.FailIsWindow = true;
        f.Emit(Create, 10);
        Check(f.Problems.Count == 1 && f.Native.ActiveHooks == 0, "Callback exceptions must be contained and stop tracking.");
    }

    private static void UnacceptedMarkers()
    {
        using var f = new Fixture(subscribe: false);
        f.Start();
        f.Open(10);
        Check(f.CaptureCount == 1 && f.UnmarkCount == 1 && f.Markers.Count == 0,
            "A candidate never delivered to a subscriber must be unmarked.");
    }

    private static void PostCaptureFailure()
    {
        using var f = new Fixture();
        f.Start();
        f.AfterCapture = () => f.Native.FailIdentity = true;
        f.Open(10);
        Check(f.UnmarkCount == 1 && f.Markers.Count == 0 && f.Native.ActiveHooks == 0 &&
            f.Problems.Count == 1, "Validation failure after capture must release only the unaccepted marker.");
    }

    private static void StopDuringCapture()
    {
        using var f = new Fixture();
        f.Start();
        f.AfterCapture = f.Tracker.Stop;
        f.Open(10);
        Check(f.Opened.Count == 0 && f.Markers.Count == 0 && f.UnmarkCount == 1,
            "Reentrant Stop must invalidate an in-flight capture before delivery.");
    }

    private static void ExpiredCandidate()
    {
        using var f = new Fixture();
        f.Start();
        f.Native.Add(10);
        f.Emit(Create, 10);
        f.Native.Clock += 120001;
        f.Emit(Show, 10);
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0, "Cleanup must not make an expired lifetime new again.");
    }

    private static void CandidateCapacity()
    {
        using var f = new Fixture();
        f.Start();
        for (int index = 1; index <= 4097; index++)
        {
            f.Native.Add(index);
            f.Emit(Create, index);
        }
        Check(f.Problems.Count == 1 && f.Native.ActiveHooks == 0, "Candidate storage must fail closed at its bounded capacity.");
    }

    private static void HookAndBaselineFailure()
    {
        using (var f = new Fixture())
        {
            f.Native.FailHookNumber = 2;
            ExpectFailure(f.Start);
            Check(f.Native.ActiveHooks == 0 && f.Problems.Count == 1, "Partial hook installation must be rolled back.");
        }
        using (var f = new Fixture())
        {
            f.Native.FailEnumeration = true;
            ExpectFailure(f.Start);
            Check(f.Native.ActiveHooks == 0 && f.Problems.Count == 1, "Baseline failure must unhook and report.");
        }
    }

    private static void FailedUnhookCanRetry()
    {
        using var f = new Fixture();
        f.Start();
        f.Native.FailUnhook = true;
        ExpectFailure(f.Tracker.Stop);
        f.Native.Add(10);
        f.Emit(Create, 10);
        f.Emit(Show, 10);
        Check(f.CaptureCount == 0 && f.Problems.Count == 1, "Failed removal must still disarm callbacks.");
        f.Native.FailUnhook = false;
        f.Tracker.Stop();
        Check(f.Native.ActiveHooks == 0, "A later Stop must retry failed hook removals.");
    }

    private static void OffThreadDispatch()
    {
        using var pump = new FakePump();
        var native = new FakeNative();
        int deliveredThread = 0;
        using var tracker = new NativeWindowTracker(native,
            (handle, _, _) => Saved(handle, 200), (_, _) => { }, pump.Invoke);
        tracker.WindowOpened += _ => deliveredThread = Environment.CurrentManagedThreadId;
        Task.Run(() => tracker.Start("session", Target)).GetAwaiter().GetResult();
        pump.Invoke(() =>
        {
            native.Add(10);
            native.Emit(Create, 10);
            native.Emit(Show, 10);
        }).GetAwaiter().GetResult();
        Task.Run(tracker.Stop).GetAwaiter().GetResult();
        Check(native.HookThreads.Count == 6 && native.HookThreads.All(thread => thread == pump.ThreadId),
            "Off-thread Start/Stop must install and remove all hooks on one message-pump thread.");
        Check(deliveredThread == pump.ThreadId, "Enrollment must run on the hook owner.");
    }

    private static void WrongThreadDispatch()
    {
        using var f = new Fixture();
        f.Start();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { ExpectFailure(f.Tracker.Stop); }
            catch (Exception error) { failure = error; }
        });
        worker.Start();
        worker.Join();
        if (failure is not null) throw failure;
        Check(f.Problems.Count == 1 && f.Native.ActiveHooks == 3,
            "An incorrectly inline worker dispatcher must not remove owner-thread hooks.");
        f.Tracker.Stop();
        Check(f.Native.ActiveHooks == 0, "Owner-thread cleanup must remain possible.");
    }

    private static void DispatcherFailure()
    {
        bool reject = true;
        var native = new FakeNative();
        int problems = 0;
        using var tracker = new NativeWindowTracker(native, (_, _, _) => null, (_, _) => { },
            action =>
            {
                if (reject) return Task.FromException(new InvalidOperationException("dispatcher unavailable"));
                action();
                return Task.CompletedTask;
            });
        tracker.Problem += _ => problems++;
        ExpectFailure(() => tracker.Start("session", Target));
        Check(problems == 1 && native.ActiveHooks == 0, "A faulted dispatcher task must be observed and reported.");
        reject = false;
    }

    private static void TraceListenerFailure()
    {
        using var listener = new ThrowingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            using var f = new Fixture();
            f.Start();
            f.Native.Add(10);
            f.Native.FailIsWindow = true;
            f.Emit(Create, 10);
            Check(f.Problems.Count == 1 && f.Native.ActiveHooks == 0,
                "A failing trace listener must not prevent error reporting or escape a native callback.");
        }
        finally { Trace.Listeners.Remove(listener); }
    }

    private static SavedWindow Saved(nint handle, int process) =>
        new(handle.ToInt64(), process, 1, "Application", new(0, 1, new(0, 0), new(0, 0), new(0, 0, 100, 100)), Target);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected the operation to fail.");
    }

    private sealed class Fixture : IDisposable
    {
        public readonly FakeNative Native = new();
        public readonly NativeWindowTracker Tracker;
        public readonly List<SavedWindow> Opened = [];
        public readonly List<string> Problems = [];
        public readonly HashSet<nint> Markers = [];
        public int CaptureCount, UnmarkCount;
        public string? CapturedToken;
        public Display? CapturedTarget;
        public bool Eligible = true, CaptureError;
        public Action? AfterCapture;

        public Fixture(bool subscribe = true)
        {
            Tracker = new(Native, Capture, (window, _) =>
            {
                UnmarkCount++;
                Markers.Remove(new nint(window.Handle));
            }, action => { action(); return Task.CompletedTask; });
            if (subscribe) Tracker.WindowOpened += Opened.Add;
            Tracker.Problem += Problems.Add;
        }

        public void Start() => Tracker.Start("session", Target);
        public void Open(nint handle) { Native.Add(handle); Emit(Create, handle); Emit(Show, handle); }
        public void Emit(uint eventId, nint handle, int objectId = 0, int childId = 0,
            uint eventThread = 50, uint? time = null, bool includeRemoved = false) =>
            Native.Emit(eventId, handle, objectId, childId, eventThread, time, includeRemoved);

        private SavedWindow? Capture(nint handle, string token, Display target)
        {
            CaptureCount++;
            CapturedToken = token;
            CapturedTarget = target;
            if (CaptureError) throw new InvalidOperationException("capture failure");
            if (!Eligible) return null;
            SavedWindow window = Saved(handle, checked((int)Native.Windows[handle].Process));
            Markers.Add(handle);
            AfterCapture?.Invoke();
            return window;
        }

        public void Dispose()
        {
            Native.FailUnhook = false;
            Tracker.Dispose();
        }
    }

    private sealed class FakeNative : IWindowLifecycleNative
    {
        public readonly Dictionary<nint, (uint Process, uint Thread, nint Root)> Windows = [];
        private readonly List<(nint Handle, uint Event, WindowLifecycleCallback Callback)> registrations = [];
        private readonly HashSet<nint> active = [];
        public readonly List<int> HookThreads = [];
        public long Clock = 1000;
        public bool FailIsWindow, FailIdentity, FailEnumeration, FailUnhook;
        public int FailHookNumber;
        public int ActiveHooks => active.Count;
        public long TickCount64 => Clock;
        public uint CurrentThreadId => (uint)Environment.CurrentManagedThreadId;
        public uint CurrentProcessId => 100;

        public void Add(nint handle, uint process = 200, uint thread = 50, nint root = 0) =>
            Windows[handle] = (process, thread, root == 0 ? handle : root);

        public nint SetHook(uint eventId, WindowLifecycleCallback callback)
        {
            if (registrations.Count + 1 == FailHookNumber) throw new InvalidOperationException("hook failure");
            nint handle = registrations.Count + 1;
            registrations.Add((handle, eventId, callback));
            active.Add(handle);
            HookThreads.Add(Environment.CurrentManagedThreadId);
            return handle;
        }

        public void RemoveHook(nint hook)
        {
            HookThreads.Add(Environment.CurrentManagedThreadId);
            if (FailUnhook) throw new InvalidOperationException("unhook failure");
            active.Remove(hook);
        }

        public void EnumerateWindows(Func<nint, bool> callback)
        {
            if (FailEnumeration) throw new InvalidOperationException("baseline failure");
            foreach (nint handle in Windows.Keys.ToArray())
                if (!callback(handle)) return;
        }

        public bool IsWindow(nint handle)
        {
            if (FailIsWindow) throw new InvalidOperationException("native callback failure");
            return Windows.ContainsKey(handle);
        }

        public nint GetRoot(nint handle) => Windows.TryGetValue(handle, out var window) ? window.Root : 0;
        public uint GetThreadProcessId(nint handle, out uint processId)
        {
            if (FailIdentity) throw new InvalidOperationException("identity failure");
            if (Windows.TryGetValue(handle, out var window))
            {
                processId = window.Process;
                return window.Thread;
            }
            processId = 0;
            return 0;
        }

        public void Emit(uint eventId, nint handle, int objectId = 0, int childId = 0,
            uint eventThread = 50, uint? time = null, bool includeRemoved = false)
        {
            uint timestamp = time ?? unchecked((uint)++Clock);
            foreach (var registration in registrations.ToArray())
                if (registration.Event == eventId && (includeRemoved || active.Contains(registration.Handle)))
                    registration.Callback(registration.Handle, eventId, handle, objectId, childId, eventThread, timestamp);
        }
    }

    private sealed class FakePump : IDisposable
    {
        private readonly BlockingCollection<Action> queue = new();
        private readonly Thread thread;
        public int ThreadId => thread.ManagedThreadId;

        public FakePump()
        {
            thread = new Thread(() =>
            {
                foreach (Action action in queue.GetConsumingEnumerable()) action();
            }) { IsBackground = true };
            thread.Start();
        }

        public Task Invoke(Action action)
        {
            if (Environment.CurrentManagedThreadId == ThreadId)
            {
                action();
                return Task.CompletedTask;
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add(() =>
            {
                try { action(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
            });
            return completion.Task;
        }

        public void Dispose()
        {
            queue.CompleteAdding();
            thread.Join();
            queue.Dispose();
        }

    }

    private sealed class ThrowingTraceListener : TraceListener
    {
        public override void Write(string? message) => throw new InvalidOperationException("trace failure");
        public override void WriteLine(string? message) => throw new InvalidOperationException("trace failure");
    }
}
