using WindowGather.Presentation;
using Xunit;

namespace WindowGather.Core.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task ConflictingRequestsRejectImmediatelyWithoutQueueing()
    {
        var (desktop, _, engine) = Fixture.Create();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        desktop.BeforeMove = () =>
        {
            started.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test gate was not released");
        };
        var coordinator = new OperationCoordinator(engine);
        Task<OperationReply> gather = coordinator.ExecuteAsync(OperationKind.GatherPointer);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(coordinator.State.IsBusy);
            var busy = await coordinator.ExecuteAsync(OperationKind.Restore);
            Assert.False(busy.Accepted);
            Assert.Contains("busy", busy.Summary);
            Assert.False((await coordinator.ExecuteAsync(OperationKind.Forget)).Accepted);
            desktop.Pointer = Fixture.A;
        }
        finally { release.Set(); }
        Assert.Empty((await gather).Problems);
        Assert.Equal(1, desktop.PointerReads);
        Assert.Equal(Fixture.B, engine.Session!.Target);
        Assert.True(coordinator.State.HasRecovery);
        Assert.False(coordinator.State.IsBusy);
        Assert.Single(desktop.Moved);
    }

    [Fact]
    public async Task RevalidatesRecoveryAndPublishesIndependentSnapshots()
    {
        var (desktop, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        Assert.False(coordinator.State.IsBusy);
        var revisions = new List<long>();
        coordinator.StateChanged += state => revisions.Add(state.Revision);
        var refreshed = await coordinator.ExecuteAsync(OperationKind.Refresh);
        Assert.Empty(refreshed.Problems);
        Assert.Equal("Displays refreshed.", refreshed.Summary);
        var snapshot = coordinator.State;
        desktop.Displays.Remove(Fixture.A);
        Assert.Equal(2, snapshot.Displays.Count);
        desktop.Displays.Add(Fixture.A);
        Assert.Empty((await coordinator.ExecuteAsync(OperationKind.GatherSelected, Fixture.B)).Problems);
        Assert.Single((await coordinator.ExecuteAsync(OperationKind.GatherSelected, Fixture.A)).Problems);
        Assert.Empty((await coordinator.ExecuteAsync(OperationKind.Restore)).Problems);
        Assert.False(coordinator.State.HasRecovery);
        Assert.Equal(revisions.Order(), revisions);
        Assert.Contains("Choose a destination", Assert.Single((await coordinator.ExecuteAsync(OperationKind.GatherSelected)).Problems));
        Assert.Single((await coordinator.ExecuteAsync((OperationKind)999)).Problems);
    }

    [Fact]
    public async Task DiskErrorsRemainVisibleAndRetryable()
    {
        var (_, store, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        await coordinator.ExecuteAsync(OperationKind.GatherSelected, Fixture.B);
        store.FailWrites = true;
        var reply = await coordinator.ExecuteAsync(OperationKind.Restore);
        Assert.Contains("IOException", Assert.Single(reply.Problems));
        Assert.True(reply.Accepted);
        Assert.Equal("The operation could not finish. Recovery data has not been discarded.", reply.Summary);
        Assert.True(coordinator.State.HasRecovery);
        Assert.False(coordinator.State.IsBusy);
        store.FailWrites = false;
        var forgotten = await coordinator.ExecuteAsync(OperationKind.Forget);
        Assert.Empty(forgotten.Problems);
        Assert.Equal("Recovery forgotten. No windows were moved.", forgotten.Summary);
        Assert.False(coordinator.State.HasRecovery);
    }

    [Fact]
    public async Task DisplayErrorsSurfaceWithoutLosingRecovery()
    {
        var (desktop, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        await coordinator.ExecuteAsync(OperationKind.GatherSelected, Fixture.B);
        desktop.FailDisplays = true;
        Assert.Contains("Display failure", Assert.Single((await coordinator.ExecuteAsync(OperationKind.Refresh)).Problems));
        Assert.True(coordinator.State.HasRecovery);
    }

    [Fact]
    public async Task CommandsInvalidateAndSelectionRemainsLockedDuringRecovery()
    {
        var (_, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        var backend = new Hotkeys();
        var shortcuts = new ShortcutController(backend, new ShortcutStore(backend));
        shortcuts.Activate(ShortcutSettings.Defaults);
        var dispatcher = new Dispatcher();
        using var model = new MainViewModel(coordinator, shortcuts, dispatcher);
        Assert.False(model.GatherCommand.CanExecute(null));
        int invalidations = 0;
        model.GatherCommand.CanExecuteChanged += (_, _) => invalidations++;
        await model.ExecuteAsync(OperationKind.Refresh);
        Assert.True(model.GatherCommand.CanExecute(null));
        Assert.True(model.IdentifyCommand.CanExecute(null));
        model.SelectedDisplay = Fixture.B;
        await model.GatherCommand.ExecuteAsync(null);
        Assert.True(model.HasRecovery);
        Assert.False(model.CanSelect);
        Assert.False(model.GatherCommand.CanExecute(null));
        Assert.True(model.RestoreCommand.CanExecute(null));
        Assert.Equal(Fixture.B, model.SelectedDisplay);
        Assert.Equal(1, model.BorrowedCount);
        await model.RestoreCommand.ExecuteAsync(null);
        Assert.True(model.CanSelect);
        Assert.False(model.HasDetails);
        Assert.StartsWith("Restored 1", model.Summary);
        Assert.True(invalidations > 0);
    }

    [Fact]
    public async Task QueuedNotificationsDispatchAndWarningsPersistOnFailure()
    {
        var (desktop, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        var backend = new Hotkeys { Blocked = ShortcutSettings.Defaults.Gather };
        var store = new ShortcutStore(backend);
        var shortcuts = new ShortcutController(backend, store);
        var dispatcher = new Dispatcher();
        using var model = new MainViewModel(coordinator, shortcuts, dispatcher, "Cannot load");
        model.UpdateShortcuts(shortcuts.Activate(ShortcutSettings.Defaults));
        Assert.True(model.HasShortcutWarning);
        var custom = new ShortcutSettings(new(6, ShortcutKey.G), new(6, ShortcutKey.R));
        store.FailWrites = true;
        Assert.Single((await model.SaveShortcutsAsync(custom)).Problems);
        Assert.Contains("Shortcut conflict", model.ShortcutWarning);
        Assert.Contains("Cannot load", model.ShortcutWarning);
        store.FailWrites = false;
        Assert.Empty((await model.SaveShortcutsAsync(custom)).Problems);
        Assert.False(model.HasShortcutWarning);
        Assert.Contains("Ctrl + Shift + G", model.ShortcutReference);
        dispatcher.Queued = true;
        desktop.Displays.Remove(Fixture.A);
        await model.ExecuteAsync(OperationKind.Refresh);
        Assert.Equal(2, model.Displays.Count);
        dispatcher.Drain();
        Assert.Single(model.Displays);
        int identify = 0, settings = 0, attention = 0;
        model.IdentifyRequested += () => identify++;
        model.ShortcutsRequested += () => settings++;
        model.AttentionRequested += () => attention++;
        model.IdentifyCommand.Execute(null);
        model.ChangeShortcutsCommand.Execute(null);
        model.ReportProblem("Actual failure");
        Assert.Equal(1, identify);
        Assert.Equal(1, settings);
        Assert.Equal(1, attention);
        Assert.True(model.HasDetails);
        dispatcher.Reject = true;
        await model.ExecuteAsync(OperationKind.Refresh);
        Assert.Equal("Actual failure", model.Details);

    }

    [Fact]
    public void ShortcutEditorPreservesAllModifierBits()
    {
        var editor = new ShortcutEditorViewModel(new(15, ShortcutKey.D7));
        Assert.True(editor.Ctrl && editor.Alt && editor.Shift && editor.Win);
        Assert.Equal(new Shortcut(15, ShortcutKey.D7), editor.GetShortcut());
        editor.SetShortcut(ShortcutSettings.Defaults.Gather);
        Assert.False(editor.Shift || editor.Win);
        Assert.Equal(ShortcutSettings.Defaults.Gather, editor.GetShortcut());
        Assert.Equal(Shortcut.SupportedKeys, editor.Keys);
    }

    [Fact]
    public async Task SubscriberFailureReleasesOperationGateAndStaleNotificationsAreIgnored()
    {
        var (_, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        void Fail(ApplicationState _) => throw new InvalidOperationException("Subscriber failure");
        coordinator.StateChanged += Fail;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync(OperationKind.Refresh));
        coordinator.StateChanged -= Fail;
        Assert.Empty((await coordinator.ExecuteAsync(OperationKind.Refresh)).Problems);
        var backend = new Hotkeys();
        var shortcuts = new ShortcutController(backend, new ShortcutStore(backend));
        var dispatcher = new Dispatcher { Queued = true };
        using var model = new MainViewModel(coordinator, shortcuts, dispatcher);
        await model.ExecuteAsync(OperationKind.Refresh);
        dispatcher.Pending.Reverse();
        dispatcher.Drain();
        Assert.False(model.IsBusy);
    }
}
