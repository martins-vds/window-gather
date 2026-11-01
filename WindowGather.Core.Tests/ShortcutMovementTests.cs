using Xunit;

namespace WindowGather.Core.Tests;

public sealed class ShortcutMovementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerDestinationIsCapturedBeforeConfirmationAndMovementRequiresApproval(bool approved)
    {
        var (desktop, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        Display original = desktop.GetPointerDisplay();
        int prompts = 0;
        var movement = new ShortcutMovement(coordinator, engine.GetPointerDisplay, (kind, target) =>
        {
            prompts++;
            Assert.Equal(OperationKind.GatherSelected, kind);
            Assert.Equal(original, target);
            desktop.Pointer = Fixture.A;
            return Task.FromResult(approved);
        });
        OperationReply reply = await movement.ExecuteAsync(OperationKind.GatherPointer);
        Assert.Equal(1, prompts);
        Assert.Equal(approved, engine.Session is not null);
        Assert.Equal(approved, reply.Accepted);
        Assert.Equal(2, desktop.PointerReads);
    }

    [Fact]
    public async Task PendingConfirmationRejectsAnotherShortcutAndCancellationDoesNotRestore()
    {
        var (desktop, store, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        var confirmation = new TaskCompletionSource<bool>();
        int prompts = 0;
        var movement = new ShortcutMovement(coordinator, engine.GetPointerDisplay, (_, _) =>
        {
            prompts++;
            return confirmation.Task;
        });
        Task<OperationReply> first = movement.ExecuteAsync(OperationKind.GatherPointer);
        Assert.True(movement.IsPending);
        Assert.False((await movement.ExecuteAsync(OperationKind.Restore)).Accepted);
        Assert.Equal(1, prompts);
        confirmation.SetResult(false);
        Assert.False((await first).Accepted);
        Assert.False(movement.IsPending);
        Assert.Null(engine.Session);
        Assert.DoesNotContain("restore", store.Log);
    }

    [Fact]
    public async Task ApprovedRestoreCompletesAndRejectedTopologyLeavesRecoveryIntact()
    {
        var (desktop, _, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        var coordinator = new OperationCoordinator(engine);
        var movement = new ShortcutMovement(coordinator, engine.GetPointerDisplay, (kind, target) =>
        {
            Assert.Equal(OperationKind.Restore, kind);
            Assert.Null(target);
            return Task.FromResult(true);
        });
        Assert.True((await movement.ExecuteAsync(OperationKind.Restore)).Accepted);
        Assert.Null(engine.Session);
        Assert.Single(desktop.Restored);

        var changed = new ShortcutMovement(coordinator, engine.GetPointerDisplay, (_, _) =>
        {
            desktop.Displays.Remove(Fixture.B);
            return Task.FromResult(true);
        });
        Assert.NotEmpty((await changed.ExecuteAsync(OperationKind.GatherPointer)).Problems);
        Assert.Null(engine.Session);
    }

    [Fact]
    public async Task ConfirmationExceptionsReleaseThePendingLatchAndInvalidKindsNeverPrompt()
    {
        var (desktop, _, engine) = Fixture.Create();
        int prompts = 0;
        var movement = new ShortcutMovement(new OperationCoordinator(engine), engine.GetPointerDisplay, (_, _) =>
        {
            prompts++;
            throw new IOException("Confirmation unavailable");
        });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => movement.ExecuteAsync(OperationKind.Forget));
        Assert.Equal(0, prompts);
        await Assert.ThrowsAsync<IOException>(() => movement.ExecuteAsync(OperationKind.GatherPointer));
        Assert.False(movement.IsPending);
    }

    [Fact]
    public async Task ShortcutCompletionIsPresentedByTheViewModel()
    {
        var (_, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        var backend = new Hotkeys();
        using var model = new WindowGather.Presentation.MainViewModel(coordinator,
            new ShortcutController(backend, new ShortcutStore(backend)), new Dispatcher());
        OperationReply reply = await coordinator.ExecuteAsync(OperationKind.GatherSelected, Fixture.B);
        model.PresentShortcutResult(reply);
        Assert.Equal(reply.Summary, model.Summary);
        Assert.True(model.HasRecovery);
    }
}
