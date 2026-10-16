using WindowGather.Presentation;
using Xunit;

namespace WindowGather.Core.Tests;

public sealed class PrimaryTests
{
    private sealed class PrimaryFixture : IPrimaryDisplay, IWindowInspector, IWindowTracker, IGatherPreferencesStore
    {
        public Desktop Desktop { get; } = new([]);
        public Store Store { get; } = new([]);
        public GatherEngine Engine { get; }
        public string PrimaryId { get; set; } = Fixture.A.Id;
        public List<string> Changes { get; } = [];
        public bool FailSwitch { get; set; }
        public bool FailTracking { get; set; }
        public bool FailPreferences { get; set; }
        public bool FailLoad { get; set; }
        public bool Tracking { get; private set; }
        public Action? AfterSwitch { get; set; }
        public Action? BeforeInspect { get; set; }
        public GatherPreferences Current { get; set; } = new();
        public event Action<SavedWindow>? WindowOpened { add { } remove { } }
        public event Action<string>? Problem { add { } remove { } }
        public PrimaryFixture() => Engine = new(Desktop, Store, this, this, this, this);
        public string GetPrimaryDisplayId() => PrimaryId;
        public void SetPrimaryDisplay(string displayId)
        {
            Changes.Add(displayId);
            if (FailSwitch) throw new InvalidOperationException("Simulated primary failure");
            int x = Desktop.Displays.Single(d => d.Id == displayId).Bounds.Left;
            int y = Desktop.Displays.Single(d => d.Id == displayId).Bounds.Top;
            foreach (Display display in Desktop.Displays.ToArray())
            {
                static Box Shift(Box b, int x, int y) => new(b.Left - x, b.Top - y, b.Right - x, b.Bottom - y);
                Desktop.Displays[Desktop.Displays.IndexOf(display)] = display with
                {
                    Bounds = Shift(display.Bounds, x, y), WorkArea = Shift(display.WorkArea, x, y)
                };
            }
            PrimaryId = displayId;
            AfterSwitch?.Invoke();
        }
        public SavedWindow Inspect(SavedWindow window, string token)
        {
            BeforeInspect?.Invoke();
            return Desktop.Windows[window.Handle];
        }
        public void Start(string token, Display target)
        {
            if (FailTracking) throw new InvalidOperationException("Simulated tracking failure");
            Tracking = true;
        }
        public void Stop() => Tracking = false;
        public GatherPreferences Load() => FailLoad ? throw new IOException("Bad preferences") : Current;
        public void Save(GatherPreferences preferences)
        {
            if (FailPreferences) throw new IOException("Options disk failure");
            Current = preferences;
        }
        public void Gather(bool follow = true)
        {
            Engine.SavePreferences(new(true, follow));
            Engine.Gather(Fixture.B);
        }
        public SavedWindow Open(long handle = 3)
        {
            var window = Fixture.Window(handle, Desktop.Displays.Single(d => d.Id == Fixture.B.Id));
            Desktop.Windows[handle] = window;
            Desktop.Markers[handle] = Engine.Session!.Token;
            Engine.TrackWindow(window, Engine.Session.Token);
            return window;
        }
    }

    [Fact]
    public void OptionsDefaultOffAndInvalidCombinationRejected()
    {
        var f = new PrimaryFixture();
        Assert.Equal(new GatherPreferences(), f.Engine.Preferences);
        Assert.Empty(f.Engine.PreferencesWarning);
        Assert.Contains("requires", Assert.Throws<ArgumentException>(() => f.Engine.SavePreferences(new(false, true))).Message);
        Assert.Empty(f.Changes);
    }

    [Fact]
    public void PrimarySwitchIsSavedBeforeMovementAndReversedBeforeBorrowedReturn()
    {
        var f = new PrimaryFixture();
        f.AfterSwitch = () =>
        {
            Assert.NotNull(f.Store.Current?.Primary);
            Assert.Empty(f.Desktop.Moved);
        };
        f.Gather(false);
        Assert.Equal(Fixture.B.Id, f.PrimaryId);
        Assert.Equal(PrimaryPhase.Applied, f.Engine.Session!.Primary!.Phase);
        f.AfterSwitch = () => Assert.Empty(f.Desktop.Restored);
        Assert.Equal(1, f.Engine.Restore().Completed);
        Assert.Equal(Fixture.A.Id, f.PrimaryId);
        Assert.Equal(new[] { Fixture.B.Id, Fixture.A.Id }, f.Changes);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void SwitchFailureStopsGatherAndRetainsOriginalRecovery()
    {
        var f = new PrimaryFixture { FailSwitch = true };
        Assert.Throws<InvalidOperationException>(() => f.Gather());
        Assert.Empty(f.Desktop.Moved);
        Assert.Equal(PrimaryPhase.Prepared, f.Store.Current!.Primary!.Phase);
        f.FailSwitch = false;
        Assert.Empty(f.Engine.Restore().Problems);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void SaveFailureAfterSwitchLeavesExplicitInterruptedRecovery()
    {
        var f = new PrimaryFixture();
        f.AfterSwitch = () => f.Store.FailWrites = true;
        Assert.Throws<IOException>(() => f.Gather());
        Assert.Empty(f.Desktop.Moved);
        Assert.Equal(PrimaryPhase.Prepared, f.Engine.Session!.Primary!.Phase);
        f.Store.FailWrites = false;
        var result = f.Engine.Restore();
        Assert.Contains(result.Problems, p => p.Contains("interrupted"));
        Assert.NotNull(f.Engine.Session);
        f.PrimaryId = Fixture.A.Id;
        Assert.Empty(f.Engine.Restore().Problems);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void PrimaryOnlyGatherRemainsRestorable()
    {
        var f = new PrimaryFixture();
        f.Desktop.Windows.Remove(1);
        f.Gather(false);
        Assert.Empty(f.Engine.Session!.Windows);
        Assert.NotNull(f.Engine.Session.Primary);
        f.Engine.Restore();
        Assert.Equal(Fixture.A.Id, f.PrimaryId);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void AlreadyPrimaryDoesNotIssueRedundantSwitch()
    {
        var f = new PrimaryFixture { PrimaryId = Fixture.B.Id };
        f.Gather();
        f.Open();
        f.Engine.Restore();
        Assert.Empty(f.Changes);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void NewFollowersMoveButResidentsAndUnrecordedWindowsDoNot()
    {
        var f = new PrimaryFixture();
        f.Gather();
        var resident = f.Desktop.Windows[2];
        f.Open();
        f.Desktop.Windows[4] = Fixture.Window(4, Fixture.B);
        Assert.True(f.Tracking);
        Assert.Equal(2, f.Engine.Restore().Completed);
        Assert.Equal(Fixture.A.Id, f.Desktop.Windows[3].Origin.Id);
        Assert.Equal(resident, f.Desktop.Windows[2]);
        Assert.Equal(Fixture.B.Id, f.Desktop.Windows[4].Origin.Id);
        Assert.False(f.Tracking);
        Assert.Empty(f.Desktop.Markers);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void UserMovedAndClosedFollowersAreSkipped()
    {
        var f = new PrimaryFixture();
        f.Gather();
        var window = f.Open();
        f.Desktop.Windows[3] = window with { Origin = Fixture.A };
        f.Open(4);
        f.Desktop.Markers.Remove(4);
        var reply = f.Engine.Restore();
        Assert.Equal(2, reply.Skipped);
        Assert.DoesNotContain(3L, f.Desktop.Moved);
        Assert.DoesNotContain(4L, f.Desktop.Moved);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void FailedFollowerRemainsRetryableWithoutRepeatingResolvedWindows()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Open();
        f.Open(4);
        f.Desktop.FailMove = 4;
        Assert.Single(f.Engine.Restore().Problems);
        Assert.Equal(4, Assert.Single(f.Engine.Session!.Primary!.Followers).Handle);
        Assert.False(f.Desktop.Markers.ContainsKey(3));
        f.Desktop.FailMove = 0;
        Assert.Empty(f.Engine.Restore().Problems);
        Assert.Equal(1, f.Desktop.Moved.Count(h => h == 3));
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void FollowerRetryKeepsItsDurableDestinationRatherThanChasingNewPrimary()
    {
        var f = new PrimaryFixture();
        var third = Fixture.A with { Id = "third", DeviceName = "Third" };
        f.Desktop.Displays.Add(third);
        f.Gather();
        f.Open();
        f.Desktop.FailMove = 3;
        Assert.Single(f.Engine.Restore().Problems);
        f.PrimaryId = third.Id;
        f.Desktop.FailMove = 0;
        Assert.Contains("primary changed", Assert.Single(f.Engine.Restore().Problems));
        Assert.Single(f.Engine.Session!.Primary!.Followers);
        f.PrimaryId = Fixture.A.Id;
        Assert.Empty(f.Engine.Restore().Problems);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void VanishedFollowerDestinationStaysPending()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Open();
        f.BeforeInspect = () => f.Desktop.Displays.RemoveAll(d => d.Id == Fixture.A.Id);
        Assert.Contains("destination is unavailable", Assert.Single(f.Engine.Restore().Problems));
        Assert.Single(f.Engine.Session!.Primary!.Followers);
    }

    [Fact]
    public void AutomaticReturnRecordsObservedExternalOwnershipRelease()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.PrimaryId = Fixture.A.Id;
        f.Engine.ObservePrimary();
        f.PrimaryId = Fixture.B.Id;
        f.Engine.ObservePrimary();
        f.Desktop.Displays.RemoveAll(d => d.Id == Fixture.B.Id);
        f.PrimaryId = Fixture.A.Id;
        f.Engine.ReturnAfterDestinationLoss(f.Desktop.Displays);
        Assert.Equal(PrimaryPhase.Released, f.Engine.Session!.Primary!.Phase);
    }

    [Fact]
    public void ManualOverrideIsRespectedIncludingObservedAwayAndBack()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Open();
        f.PrimaryId = Fixture.A.Id;
        f.Engine.ObservePrimary();
        f.PrimaryId = Fixture.B.Id;
        f.Engine.ObservePrimary();
        f.Engine.Restore();
        Assert.Single(f.Changes);
        Assert.Equal(Fixture.B.Id, f.PrimaryId);
        Assert.Equal(Fixture.B.Id, f.Desktop.Windows[3].Origin.Id);
    }

    [Fact]
    public void CurrentManualOverrideWithoutEventIsAlsoRespected()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.PrimaryId = Fixture.A.Id;
        f.Engine.Restore();
        Assert.Single(f.Changes);
    }

    [Fact]
    public void PrimaryFailureStillReturnsBorrowedButRetainsFollowers()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Open();
        f.FailSwitch = true;
        Assert.Contains(f.Engine.Restore().Problems, p => p.StartsWith("Primary display:"));
        Assert.Single(f.Desktop.Restored);
        Assert.Single(f.Engine.Session!.Primary!.Followers);
        Assert.Empty(f.Engine.Session.Windows);
        f.FailSwitch = false;
        f.Engine.Restore();
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void MissingOriginalPrimaryRetainsIndependentRecovery()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Open();
        f.Desktop.Displays.RemoveAll(d => d.Id == Fixture.A.Id);
        Assert.Equal(2, f.Engine.Restore().Problems.Count);
        Assert.NotNull(f.Engine.Session);
        f.Desktop.Displays.Add(Fixture.A);
        Assert.Empty(f.Engine.Restore().Problems);
        Assert.Null(f.Engine.Session);
    }

    [Fact]
    public void AutomaticReturnDoesNotRevertPrimaryOrFollowAndCannotClearPrimaryOnlySession()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Open();
        f.Desktop.Displays.RemoveAll(d => d.Id == Fixture.B.Id);
        f.PrimaryId = Fixture.A.Id;
        Assert.Equal(1, f.Engine.ReturnAfterDestinationLoss(f.Desktop.Displays).Completed);
        Assert.Single(f.Changes);
        Assert.NotNull(f.Engine.Session?.Primary);
        Assert.Single(f.Engine.Session.Primary.Followers);
        Assert.Empty(f.Engine.Session.Windows);
    }

    [Fact]
    public void ForgetNeverRevertsPrimaryAndCleansFollowerMarkers()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Open();
        f.Engine.Forget();
        Assert.Single(f.Changes);
        Assert.Equal(Fixture.B.Id, f.PrimaryId);
        Assert.Empty(f.Desktop.Markers);
        Assert.Null(f.Store.Current);
    }

    [Fact]
    public void RestartDoesNotGuessPrimaryOwnershipOrNewFollowers()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Open();
        var restarted = new GatherEngine(f.Desktop, f.Store, f, f, f);
        Assert.Contains(restarted.Restore().Problems, p => p.Contains("interrupted"));
        Assert.True(restarted.Session!.Primary!.TrackingInterrupted);
        f.PrimaryId = Fixture.A.Id;
        Assert.Equal(1, restarted.Restore().Completed);
        Assert.Null(restarted.Session);
    }

    [Fact]
    public void LegacyConsumerCannotStripExtendedRestore()
    {
        var f = new PrimaryFixture();
        f.Gather();
        var legacy = new GatherEngine(f.Desktop, f.Store);
        Assert.Contains("WinUI", Assert.Throws<InvalidOperationException>(() => legacy.Restore()).Message);
        Assert.NotNull(f.Store.Current!.Primary);
    }

    [Fact]
    public void FailedMembershipWriteUnmarksCandidateAndShowsPersistentProblem()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Store.FailWrites = true;
        Assert.Throws<IOException>(() => f.Open());
        Assert.False(f.Desktop.Markers.ContainsKey(3));
        Assert.False(f.Tracking);
        Assert.Contains("tracking could not be saved", f.Engine.RecoveryWarning(f.Desktop.Displays));
    }

    [Fact]
    public void DuplicateBorrowedAndPostRestoreCandidatesAreNotEnrolled()
    {
        var f = new PrimaryFixture();
        f.Gather();
        var window = f.Open();
        f.Engine.TrackWindow(window, f.Engine.Session!.Token);
        Assert.Single(f.Engine.Session.Primary!.Followers);
        f.Engine.TrackWindow(f.Desktop.Windows[1], f.Engine.Session.Token);
        Assert.Single(f.Engine.Session.Primary.Followers);
        f.FailSwitch = true;
        f.Engine.Restore();
        f.Open(4);
        Assert.Single(f.Engine.Session!.Primary!.Followers);
        Assert.False(f.Desktop.Markers.ContainsKey(4));
    }

    [Fact]
    public void TrackingStartupFailureIsVisibleAndDurable()
    {
        var f = new PrimaryFixture { FailTracking = true };
        f.Engine.SavePreferences(new(true, true));
        Assert.Contains(f.Engine.Gather(Fixture.B).Problems, p => p.Contains("tracking could not start"));
        Assert.True(f.Store.Current!.Primary!.TrackingInterrupted);
    }

    [Fact]
    public void TrackingProblemStopsAndPersistsInterruption()
    {
        var f = new PrimaryFixture();
        f.Gather();
        var result = f.Engine.ReportTrackingProblem("Event hook lost");
        Assert.Contains("Event hook lost", Assert.Single(result.Problems));
        Assert.Equal("New-window tracking needs attention.", result.Summary);
        Assert.False(f.Tracking);
        Assert.True(f.Store.Current!.Primary!.TrackingInterrupted);
        Assert.Contains("interrupted", f.Engine.RecoveryWarning(f.Desktop.Displays));
    }

    [Fact]
    public void PreferencesAreTransactionalAndLockedDuringRecovery()
    {
        var f = new PrimaryFixture { FailPreferences = true };
        Assert.Throws<IOException>(() => f.Engine.SavePreferences(new(true)));
        Assert.False(f.Engine.Preferences.MakeDestinationPrimary);
        f.FailPreferences = false;
        f.Gather();
        Assert.Throws<InvalidOperationException>(() => f.Engine.SavePreferences(new()));
    }

    [Fact]
    public void ObservationFailureRequiresManualOwnershipRecovery()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Desktop.FailDisplays = true;
        Assert.Throws<InvalidOperationException>(() => f.Engine.ObservePrimary());
        f.Desktop.FailDisplays = false;
        Assert.Contains(f.Engine.Restore().Problems, p => p.Contains("interrupted"));
        f.PrimaryId = Fixture.A.Id;
        Assert.Empty(f.Engine.Restore().Problems);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnershipDecisionSurvivesFailedJournalWrite(bool uncertain)
    {
        var f = new PrimaryFixture();
        f.Gather();
        if (uncertain)
        {
            f.Desktop.FailDisplays = true;
            Assert.Throws<InvalidOperationException>(() => f.Engine.ObservePrimary());
            f.Desktop.FailDisplays = false;
        }
        else
        {
            f.PrimaryId = Fixture.A.Id;
            f.Engine.ObservePrimary();
            f.PrimaryId = Fixture.B.Id;
        }
        f.Store.FailWrites = true;
        Assert.Throws<IOException>(() => f.Engine.Restore());
        f.Store.FailWrites = false;
        var result = f.Engine.Restore();
        if (uncertain)
        {
            Assert.Contains(result.Problems, p => p.Contains("interrupted"));
            Assert.NotNull(f.Engine.Session);
        }
        else
        {
            Assert.Empty(result.Problems);
            Assert.Null(f.Engine.Session);
        }
        Assert.Single(f.Changes);
        Assert.Equal(Fixture.B.Id, f.PrimaryId);
    }

    [Fact]
    public void ManualThirdPrimaryWithoutObservationIsNotOverwritten()
    {
        var f = new PrimaryFixture();
        var third = Fixture.A with { Id = "third", DeviceName = "Third" };
        f.Desktop.Displays.Add(third);
        f.Gather();
        f.Open();
        f.PrimaryId = third.Id;
        Assert.Empty(f.Engine.Restore().Problems);
        Assert.Single(f.Changes);
        Assert.Equal(third.Id, f.Desktop.Windows[3].Origin.Id);
    }

    [Fact]
    public void OwnChangesAreFlaggedAndPrimaryStatusIsExposed()
    {
        var f = new PrimaryFixture();
        f.AfterSwitch = () =>
        {
            Assert.True(f.Engine.IsChangingPrimary);
            f.Engine.ObservePrimary();
        };
        f.Gather();
        Assert.False(f.Engine.IsChangingPrimary);
        Assert.Equal(Fixture.B.Id, f.Engine.GetPrimaryDisplayId());
        Assert.Equal(Fixture.B.Id, Assert.Single(f.Engine.GetDisplays(), d => d.IsPrimary).Id);
        Assert.Contains("Primary", f.Engine.GetDisplays().Single(d => d.IsPrimary).ToString());
        Assert.True(Guid.TryParse(f.Engine.Session!.Token, out _));
        f.Engine.Restore();
        Assert.False(f.Engine.IsChangingPrimary);
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("inspector")]
    [InlineData("tracker")]
    public void MissingCapabilitiesRefuseOptionsAndLoadedGatherBeforeCapturing(string missing)
    {
        var f = new PrimaryFixture { Current = new(true, true) };
        var engine = new GatherEngine(f.Desktop, f.Store, missing == "primary" ? null : f,
            missing == "inspector" ? null : f, missing == "tracker" ? null : f, f);
        Assert.Contains("unavailable", Assert.Throws<InvalidOperationException>(() => engine.SavePreferences(new(true, true))).Message);
        Assert.Contains("cannot safely", Assert.Throws<InvalidOperationException>(() => engine.Gather(Fixture.B)).Message);
        Assert.Empty(f.Desktop.Markers);
        Assert.Empty(f.Desktop.Moved);
    }

    [Fact]
    public void AbsentOptionsStorageAndInvalidLoadedOptionsAreExplicit()
    {
        var f = new PrimaryFixture { Current = new(false, true) };
        var invalid = new GatherEngine(f.Desktop, f.Store, f, f, f, f);
        Assert.Contains("Options are off", invalid.PreferencesWarning);
        Assert.Equal(new(), invalid.Preferences);
        var unavailable = new GatherEngine(f.Desktop, f.Store);
        Assert.Contains("storage is unavailable", Assert.Throws<InvalidOperationException>(() => unavailable.SavePreferences(new())).Message);
        unavailable.ObservePrimary();
    }

    [Fact]
    public void TrackingIsQuietAndSavesCurrentPlacementBeforeMovement()
    {
        var f = new PrimaryFixture();
        f.Gather();
        SavedWindow window = f.Open();
        var quiet = f.Engine.TrackWindow(window, f.Engine.Session!.Token);
        Assert.False(quiet.Announce);
        Assert.Empty(quiet.Summary);
        Assert.DoesNotContain("interrupted", f.Engine.RecoveryWarning(f.Desktop.Displays));
        var changed = window with { Placement = window.Placement with { Normal = new(10, 20, 450, 360) } };
        f.Desktop.Windows[3] = changed;
        f.Desktop.BeforeMove = () =>
            Assert.Equal(changed.Placement, Assert.Single(f.Store.Current!.Primary!.Followers).Placement);
        Assert.Empty(f.Engine.Restore().Problems);
        Assert.Empty(f.Desktop.Markers);
    }

    [Fact]
    public void ObservationFromForgottenSessionCannotReleaseNextSessionsOwnership()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.PrimaryId = Fixture.A.Id;
        f.Engine.ObservePrimary();
        f.Engine.Forget();
        f.Engine.Gather(f.Engine.GetDisplays().Single(d => d.Id == Fixture.B.Id));
        f.Engine.Restore();
        Assert.Equal(Fixture.A.Id, f.PrimaryId);
        Assert.Equal(3, f.Changes.Count);
    }

    [Fact]
    public void ConfirmedFollowersUseCurrentOverriddenPrimaryRatherThanBorrowedOrigin()
    {
        var f = new PrimaryFixture();
        var third = Fixture.A with { Id = "third", DeviceName = "Third" };
        f.Desktop.Displays.Add(third);
        f.Gather();
        f.Open();
        f.PrimaryId = third.Id;
        f.Engine.ObservePrimary();
        f.Engine.Restore();
        Assert.Single(f.Changes);
        Assert.Equal(third.Id, f.Desktop.Windows[3].Origin.Id);
        Assert.Equal(Fixture.A.Id, f.Desktop.Windows[1].Origin.Id);
    }

    [Fact]
    public void ReusedFollowerIdentityIsNotMoved()
    {
        var f = new PrimaryFixture();
        f.Gather();
        var window = f.Open();
        f.Desktop.Windows[3] = window with { ProcessStartedUtcTicks = window.ProcessStartedUtcTicks + 1 };
        Assert.Equal(1, f.Engine.Restore().Skipped);
        Assert.DoesNotContain(3L, f.Desktop.Moved);
    }

    [Fact]
    public void TrackingLimitStopsWithVisibleRecoveryRatherThanWritingUnreadableFile()
    {
        var f = new PrimaryFixture();
        f.Gather();
        f.Engine.Session!.Primary = f.Engine.Session.Primary! with
        {
            Followers = Enumerable.Range(100, 10000).Select(h => Fixture.Window(h, Fixture.B)).ToList()
        };
        f.Open();
        Assert.False(f.Tracking);
        Assert.True(f.Engine.Session.Primary.TrackingInterrupted);
        Assert.Equal(10000, f.Engine.Session.Primary.Followers.Count);
        Assert.Contains("10,000", f.Engine.RecoveryWarning(f.Desktop.Displays));
    }

    [Fact]
    public void BrokenPreferencesUseVisibleOffDefaultsAndRepairClearsWarning()
    {
        var f = new PrimaryFixture { FailLoad = true };
        var engine = new GatherEngine(f.Desktop, f.Store, f, f, f, f);
        Assert.False(engine.Preferences.MakeDestinationPrimary);
        Assert.Contains("Cannot load", engine.PreferencesWarning);
        f.FailLoad = false;
        engine.SavePreferences(new(true));
        Assert.Empty(engine.PreferencesWarning);
    }

    [Fact]
    public async Task CoordinatorAndViewModelShareOptionsAndPrimaryOnlyRecovery()
    {
        var f = new PrimaryFixture();
        var coordinator = new OperationCoordinator(f.Engine);
        var hotkeys = new Hotkeys();
        using var model = new MainViewModel(coordinator, new ShortcutController(hotkeys, new ShortcutStore(hotkeys)), new Dispatcher());
        await model.SaveGatherOptionsAsync(new(true, true));
        Assert.Equal("Gathering options saved.", model.Summary);
        Assert.True(model.CanFollowOptions);
        await model.ExecuteAsync(OperationKind.GatherPointer);
        Assert.True(model.HasPrimaryRecovery);
        Assert.False(model.CanFollowOptions);
        var opened = f.Open();
        await coordinator.NotifyWindowOpenedAsync(opened);
        Assert.Equal(1, coordinator.State.FollowerCount);
        await model.TrackingProblemAsync("Lost event source");
        Assert.True(model.HasDetails);
        await model.ExecuteAsync(OperationKind.Restore);
        Assert.False(model.HasRecovery);
    }

    [Fact]
    public async Task CoordinatorWithoutSessionIgnoresLateCandidateAndForgetNamesPrimaryState()
    {
        var f = new PrimaryFixture();
        var coordinator = new OperationCoordinator(f.Engine);
        var late = await coordinator.NotifyWindowOpenedAsync(Fixture.Window(9));
        Assert.False(late.Accepted);
        Assert.False(late.Announce);
        Assert.Empty(late.Summary);
        f.Gather();
        var forgotten = await coordinator.ExecuteAsync(OperationKind.Forget);
        Assert.Equal("Recovery forgotten. No windows or primary-display settings were changed.", forgotten.Summary);
    }
}
