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

    [Fact]
    public void MissingPhysicalOriginRemainsRetryable()
    {
        var (desktop, _, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        desktop.Displays.Remove(Fixture.A);
        string problem = Assert.Single(engine.Restore().Problems);
        Assert.Contains("Reconnect", problem);
        Assert.Contains(Fixture.A.DeviceName, problem);
        Assert.Contains("click Restore again", problem);
        Assert.Empty(desktop.Restored);
        Assert.Single(engine.Session!.Windows);
        desktop.Displays.Clear();
        desktop.Displays.AddRange([Fixture.A, Fixture.B]);
        Assert.Equal(1, engine.Restore().Completed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ChangedOriginFitsSavedPlacementAndPreservesState(uint state)
    {
        var (desktop, store, engine) = Fixture.Create();
        desktop.Windows[1] = Fixture.Window(1) with
        {
            Placement = Fixture.Place(state) with { Min = new(5, 8), Max = new(9, 12) }
        };
        engine.Gather(Fixture.B);
        SavedWindow original = engine.Session!.Windows[0];
        Display current = Fixture.A with { Bounds = new(300, 100, 620, 380), WorkArea = new(300, 120, 620, 360) };
        desktop.Displays[0] = current;
        store.FailWrites = true;
        Assert.Throws<IOException>(() => engine.Restore());
        SavedWindow returned = desktop.Windows[1];
        Assert.Equal(current, returned.Origin);
        Assert.Equal(new Box(300, 100, 620, 340), returned.Placement.Normal);
        Assert.Equal(state, returned.Placement.ShowCommand);
        Assert.Equal(original.Placement.Flags, returned.Placement.Flags);
        Assert.Equal(new Position(-1, -1), returned.Placement.Min);
        Assert.Equal(new Position(-1, -1), returned.Placement.Max);
        Assert.Equal(original, Assert.Single(engine.Session.Windows));
        Assert.Equal(original, Assert.Single(store.Current!.Windows));
        Assert.Single(desktop.Markers);
        store.FailWrites = false;
        Assert.Equal(1, engine.Restore().Completed);
    }

    [Fact]
    public void UnchangedOriginRetainsExactPlacementIncludingMinMax()
    {
        var (desktop, _, engine) = Fixture.Create();
        SavedWindow original = Fixture.Window(1) with { Placement = Fixture.Place(2) with { Min = new(7, 9), Max = new(11, 13) } };
        desktop.Windows[1] = original;
        engine.Gather(Fixture.B);
        Assert.Equal(1, engine.Restore().Completed);
        Assert.Equal(original, desktop.Windows[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundsOrWorkAreaChangesIndependentlyAdaptRestoration(bool work)
    {
        var (desktop, _, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        Display current = work ? Fixture.A with { WorkArea = new(-1920, 40, 0, 1080) }
            : Fixture.A with { Bounds = new(-1920, -100, 0, 980) };
        desktop.Displays[0] = current;
        Assert.Equal(1, engine.Restore().Completed);
        Assert.Equal(current, desktop.Windows[1].Origin);
        Assert.Equal(new Box(-1820, work ? 100 : 0, -1320, work ? 500 : 400), desktop.Windows[1].Placement.Normal);
    }

    [Fact]
    public void AutomaticReturnSavesDecisionBeforeMovingAndExcludesResidentsAndNewWindows()
    {
        var (desktop, store, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        desktop.Windows[3] = Fixture.Window(3, Fixture.B);
        SavedWindow resident = desktop.Windows[2];
        desktop.Displays.Remove(Fixture.B);
        var result = engine.ReturnAfterDestinationLoss(desktop.Displays);
        Assert.Equal(1, result.Completed);
        Assert.StartsWith("Destination disconnected.", result.Summary);
        Assert.Equal(new[] { "save", "restore", "clear", "unmark" }, store.Log.TakeLast(4));
        Assert.Equal(new long[] { 1 }, desktop.Restored);
        Assert.Equal(resident, desktop.Windows[2]);
        Assert.Equal(Fixture.B, desktop.Windows[3].Origin);
        Assert.Null(engine.Session);
    }

    [Fact]
    public void OriginLossDoesNotReturnWhileDestinationIsAvailable()
    {
        var (desktop, store, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        desktop.Displays.Remove(Fixture.A);
        int writes = store.Log.Count;
        Assert.False(engine.ReturnAfterDestinationLoss(desktop.Displays).Announce);
        Assert.Equal(writes, store.Log.Count);
        Assert.Empty(desktop.Restored);
        Assert.False(engine.Session!.AutomaticReturnAttempted);
        Assert.Contains("1 borrowed windows", engine.RecoveryWarning(desktop.Displays));
    }

    [Fact]
    public void DestinationReconnectedBeforeDecisionDoesNotMoveWindows()
    {
        var (desktop, _, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        Assert.False(engine.ReturnAfterDestinationLoss([Fixture.A]).Announce);
        Assert.Empty(desktop.Restored);
        Assert.False(engine.Session!.AutomaticReturnAttempted);
    }

    [Fact]
    public void UnavailableOriginsAndFailedReturnsPersistAcrossReconnectAndRestart()
    {
        var (desktop, store, engine) = Fixture.Create();
        Display other = Fixture.A with { Id = "physical-c", DeviceName = "Other" };
        desktop.Windows[3] = Fixture.Window(3, other);
        engine.Gather(Fixture.B);
        SavedWindow[] originals = engine.Session!.Windows.ToArray();
        desktop.Displays.Remove(Fixture.B);
        desktop.FailRestore = 1;
        var result = engine.ReturnAfterDestinationLoss(desktop.Displays);
        Assert.Equal(2, result.Problems.Count);
        Assert.Equal(originals, engine.Session!.Windows);
        Assert.True(store.Current!.AutomaticReturnAttempted);
        Assert.Contains("Reconnection will not move", engine.RecoveryWarning(desktop.Displays));
        desktop.FailRestore = 0;
        desktop.Displays.Add(other);
        var restarted = new GatherEngine(desktop, store);
        Assert.False(restarted.ReturnAfterDestinationLoss(desktop.Displays).Announce);
        Assert.Empty(desktop.Restored);
        Assert.Equal(2, restarted.Restore().Completed);
        Assert.Null(restarted.Session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedAutomaticDecisionOrCompletionWritePreservesAllRecovery(bool completion)
    {
        var (desktop, store, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        GatherSession original = engine.Session!;
        desktop.Displays.Remove(Fixture.B);
        if (completion) desktop.BeforeRestore = _ => store.FailWrites = true;
        else store.FailWrites = true;
        Assert.Throws<IOException>(() => engine.ReturnAfterDestinationLoss(desktop.Displays));
        Assert.Equal(original.Token, engine.Session!.Token);
        Assert.Equal(original.Windows, engine.Session.Windows);
        Assert.Equal(completion, engine.Session.AutomaticReturnAttempted);
        Assert.Single(desktop.Markers);
        Assert.Equal(completion ? 1 : 0, desktop.Restored.Count);
        store.FailWrites = false;
        desktop.BeforeRestore = null;
        if (completion) Assert.False(new GatherEngine(desktop, store).ReturnAfterDestinationLoss(desktop.Displays).Announce);
        else
        {
            Assert.False(engine.ReturnAfterDestinationLoss(desktop.Displays).Announce);
            Assert.Empty(desktop.Restored);
            Assert.Contains("Reconnection will not move", engine.RecoveryWarning(desktop.Displays));
            Assert.Equal(1, engine.Restore().Completed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartObservesBaselineBeforeRespondingToAFutureDisconnect(bool destinationPresent)
    {
        var (desktop, store, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        if (!destinationPresent) desktop.Displays.Remove(Fixture.B);
        var restarted = new GatherEngine(desktop, store);
        Assert.False(restarted.ReturnAfterDestinationLoss(desktop.Displays).Announce);
        Assert.Empty(desktop.Restored);
        if (destinationPresent)
        {
            desktop.Displays.Remove(Fixture.B);
            Assert.Equal(1, restarted.ReturnAfterDestinationLoss(desktop.Displays).Completed);
        }
        else
        {
            Assert.Contains("Reconnection will not move", restarted.RecoveryWarning(desktop.Displays));
            desktop.Displays.Add(Fixture.B);
            Assert.False(restarted.ReturnAfterDestinationLoss(desktop.Displays).Announce);
            desktop.Displays.Remove(Fixture.B);
            Assert.False(restarted.ReturnAfterDestinationLoss(desktop.Displays).Announce);
            Assert.Equal(1, restarted.Restore().Completed);
        }
    }

    [Fact]
    public void ReconnectingOriginDuringAutomaticBatchWaitsForExplicitRestore()
    {
        var (desktop, store, engine) = Fixture.Create();
        Display other = Fixture.A with { Id = "physical-c" };
        desktop.Windows[3] = Fixture.Window(3, other);
        engine.Gather(Fixture.B);
        desktop.Displays.Remove(Fixture.B);
        desktop.BeforeRestore = _ => desktop.Displays.Add(other);
        var result = engine.ReturnAfterDestinationLoss(desktop.Displays);
        Assert.Equal(1, result.Completed);
        Assert.Single(result.Problems);
        Assert.Equal(3, Assert.Single(store.Current!.Windows).Handle);
        Assert.True(store.Current.AutomaticReturnAttempted);
        desktop.BeforeRestore = null;
        Assert.Equal(1, engine.Restore().Completed);
    }

    [Fact]
    public void AllDisplaysUnavailableDoesNotChooseASubstituteOrOverwriteReturnPoints()
    {
        var (desktop, store, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        SavedWindow original = engine.Session!.Windows[0];
        desktop.Displays.Clear();
        string problem = Assert.Single(engine.ReturnAfterDestinationLoss(desktop.Displays).Problems);
        Assert.Contains(Fixture.A.DeviceName, problem);
        Assert.Contains("then click Restore", problem);
        Assert.Empty(desktop.Restored);
        Assert.Equal(original, Assert.Single(store.Current!.Windows));
        Assert.True(store.Current.AutomaticReturnAttempted);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("bounds-width")]
    [InlineData("bounds-height")]
    [InlineData("work-width")]
    [InlineData("work-height")]
    [InlineData("duplicate")]
    public void InvalidInventoryDoesNotDiscardRecovery(string invalid)
    {
        var (desktop, _, engine) = Fixture.Create();
        engine.Gather(Fixture.B);
        desktop.Displays[0] = invalid switch
        {
            "id" => Fixture.A with { Id = "" },
            "bounds-width" => Fixture.A with { Bounds = new(0, 0, 0, 100) },
            "bounds-height" => Fixture.A with { Bounds = new(0, 0, 100, 0) },
            "work-width" => Fixture.A with { WorkArea = new(0, 0, 0, 100) },
            "work-height" => Fixture.A with { WorkArea = new(0, 0, 100, 0) },
            _ => Fixture.B
        };
        Assert.Contains("inventory is invalid or ambiguous", Assert.Throws<InvalidOperationException>(() => engine.Restore()).Message);
        Assert.Single(engine.Session!.Windows);
        Assert.Empty(desktop.Restored);
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
