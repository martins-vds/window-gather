using Xunit;

namespace WindowGather.Core.Tests;

public sealed class EngineTests
{
    [Fact]
    public void SavesBeforeMovingAndClearsBeforeUnmarking()
    {
        var (desktop, store, engine) = Fixture.Create();
        var result = engine.Gather(Fixture.B);
        Assert.Equal(1, result.Completed);
        Assert.Equal(new[] { "capture", "save", "mark", "move" }, store.Log);
        Assert.Equal(new long[] { 1 }, desktop.Moved);
        Assert.Equal(Fixture.B, engine.Session!.Target);
        Assert.Single(engine.Session.Windows);
        Assert.Empty(result.Problems);
        Assert.Contains("Windows already on the target were left untouched", result.Summary);
        var restored = engine.Restore();
        Assert.Equal(1, restored.Completed);
        Assert.EndsWith("The session is complete.", restored.Summary);
        Assert.Equal(new[] { "restore", "clear", "unmark" }, store.Log.TakeLast(3));
        Assert.Null(engine.Session);
        Assert.Null(store.Current);
    }

    [Fact]
    public void ResidentsAndNewWindowsAreNeverRestored()
    {
        var (desktop, _, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        var changed = desktop.Windows[2] with { Placement = Fixture.Place(3) };
        desktop.Windows[2] = changed;
        desktop.Windows[3] = Fixture.Window(3, Fixture.B);
        engine.Restore();
        Assert.Equal(changed, desktop.Windows[2]);
        Assert.Equal(new long[] { 1 }, desktop.Restored);
    }

    [Fact]
    public void ActiveRecoveryRejectsSecondGather()
    {
        var (_, store, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        string token = store.Current!.Token;
        Assert.Contains("Restore the current session", Assert.Throws<InvalidOperationException>(() => engine.Gather(Fixture.A)).Message);
        Assert.Equal(token, store.Current.Token);
    }

    [Fact]
    public void SaveFailureUnmarksAndDoesNotMove()
    {
        var (desktop, store, engine) = Fixture.Create();
        store.FailWrites = true;
        Assert.Throws<IOException>(() => engine.Gather(Fixture.B));
        Assert.Empty(desktop.Moved);
        Assert.Empty(desktop.Markers);
        Assert.Null(engine.Session);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("bounds")]
    [InlineData("work")]
    public void ChangedDestinationRejectsGather(string change)
    {
        var (desktop, _, engine) = Fixture.Create();
        if (change == "absent") desktop.Displays.Remove(Fixture.B);
        else desktop.Displays[1] = change == "bounds"
            ? Fixture.B with { Bounds = new(0, 0, 1800, 1000) }
            : Fixture.B with { WorkArea = new(0, 0, 1920, 1040) };
        Assert.Contains("selected display changed", Assert.Throws<InvalidOperationException>(() => engine.Gather(Fixture.B)).Message);
        Assert.Empty(desktop.Moved);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("bounds")]
    [InlineData("work")]
    public void OriginalPhysicalLayoutIsRequiredForRestore(string change)
    {
        var (desktop, _, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        if (change == "absent") desktop.Displays.Remove(Fixture.A);
        else desktop.Displays[0] = change == "bounds"
            ? Fixture.A with { Bounds = new(-1920, -100, 0, 980) }
            : Fixture.A with { WorkArea = new(-1920, 40, 0, 1080) };
        string problem = Assert.Single(engine.Restore().Problems);
        Assert.Contains(change == "absent" ? "Reconnect" : "layout", problem);
        Assert.Contains(Fixture.A.DeviceName, problem);
        Assert.Contains(change == "absent" ? "click Restore again" : "position, resolution, and taskbar arrangement", problem);
        Assert.Empty(desktop.Restored);
        Assert.Single(engine.Session!.Windows);
        desktop.Displays.Clear();
        desktop.Displays.AddRange([Fixture.A, Fixture.B]);
        Assert.Equal(1, engine.Restore().Completed);
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("closed")]
    [InlineData("pid")]
    [InlineData("start")]
    [InlineData("class")]
    public void ChangedIdentityIsSkipped(string change)
    {
        var (desktop, _, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        switch (change)
        {
            case "marker": desktop.Markers.Clear(); break;
            case "closed": desktop.Windows.Remove(1); break;
            case "pid": desktop.Windows[1] = desktop.Windows[1] with { ProcessId = 9 }; break;
            case "start": desktop.Windows[1] = desktop.Windows[1] with { ProcessStartedUtcTicks = 9 }; break;
            case "class": desktop.Windows[1] = desktop.Windows[1] with { ClassName = "Other" }; break;
        }
        var result = engine.Restore();
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Completed);
        Assert.Empty(desktop.Restored);
        Assert.Null(engine.Session);
    }

    [Fact]
    public void RestartRetainsRecovery()
    {
        var (desktop, store, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        var restarted = new GatherEngine(desktop, store);
        Assert.Equal(1, restarted.Restore().Completed);
    }

    [Fact]
    public void PartialRestorePersistsOnlyRemainingBeforeUnmarking()
    {
        var (desktop, store, engine) = Fixture.Create();
        desktop.Windows[3] = Fixture.Window(3);
        engine.Gather(Fixture.B);
        desktop.FailRestore = 3;
        var result = engine.Restore();
        Assert.Equal(1, result.Completed);
        Assert.Single(result.Problems);
        Assert.Contains("Window 3: Restore failure", result.Problems);
        Assert.EndsWith("1 still need restoration.", result.Summary);
        Assert.Equal(3, Assert.Single(engine.Session!.Windows).Handle);
        Assert.Equal(new[] { "save", "unmark" }, store.Log.TakeLast(2));
        Assert.False(desktop.Markers.ContainsKey(1));
        Assert.True(desktop.Markers.ContainsKey(3));
        desktop.FailRestore = 0;
        Assert.Equal(1, engine.Restore().Completed);
        Assert.Equal(new long[] { 1, 3 }, desktop.Restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCompletionWritePreservesReturnPointsAndMarkers(bool partial)
    {
        var (desktop, store, engine) = Fixture.Create();
        if (partial) desktop.Windows[3] = Fixture.Window(3);
        engine.Gather(Fixture.B);
        string token = engine.Session!.Token;
        DateTime created = engine.Session.CreatedUtc;
        if (partial) desktop.FailRestore = 3;
        store.FailWrites = true;
        Assert.Throws<IOException>(() => engine.Restore());
        Assert.NotNull(engine.Session);
        Assert.Equal(partial ? 2 : 1, engine.Session.Windows.Count);
        Assert.Equal(token, desktop.Markers[1]);
        store.FailWrites = false;
        if (partial)
        {
            engine.Restore();
            Assert.Equal(created, store.Current!.CreatedUtc);
            Assert.Equal(token, store.Current.Token);
            desktop.FailRestore = 0;
        }
        engine.Restore();
        Assert.Null(engine.Session);
    }

    [Fact]
    public void FailedMovesRemainRecoverableAndWarningsAreNotLost()
    {
        var (desktop, _, engine) = Fixture.Create();
        desktop.FailMove = 1;
        desktop.Warnings.Add("Capture warning");
        var result = engine.Gather(Fixture.B);
        Assert.Equal(0, result.Completed);
        Assert.Equal(2, result.Problems.Count);
        Assert.Contains("Window 1: Move failure", result.Problems);
        Assert.NotNull(engine.Session);
        Assert.Equal(1, engine.Restore().Completed);
    }

    [Fact]
    public void ForgetClearsBeforeUnmarkingWithoutMovement()
    {
        var (desktop, store, engine) = Fixture.Create();
        engine.Forget();
        engine.Gather(Fixture.B);
        store.FailWrites = true;
        Assert.Throws<IOException>(engine.Forget);
        Assert.NotNull(engine.Session);
        Assert.Single(desktop.Markers);
        store.FailWrites = false;
        engine.Forget();
        Assert.Equal(new[] { "clear", "unmark" }, store.Log.TakeLast(2));
        Assert.Empty(desktop.Restored);
        Assert.Null(engine.Session);
    }

    [Fact]
    public void NoImportsCreatesNoRecoveryAndKeepsScanWarnings()
    {
        var (desktop, store, engine) = Fixture.Create();
        desktop.Windows.Remove(1);
        desktop.Warnings.Add("Cannot capture");
        var result = engine.Gather(Fixture.B);
        Assert.Equal(0, result.Completed);
        Assert.Single(result.Problems);
        Assert.Null(engine.Session);
        Assert.Null(store.Current);
        Assert.Equal("No movable windows were found on the other displays.", result.Summary);
        Assert.Contains("no active gather session", Assert.Throws<InvalidOperationException>(() => engine.Restore()).Message);
        Assert.Equal(desktop.Displays, engine.GetDisplays());
        Assert.Equal(Fixture.B, engine.GetPointerDisplay());
    }
}
