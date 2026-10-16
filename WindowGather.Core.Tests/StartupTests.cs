using WindowGather.Presentation;
using Xunit;

namespace WindowGather.Core.Tests;

public sealed class StartupTests
{
    [Fact]
    public void RefreshReflectsRegistrationWithoutWritingAndSetVerifiesBothDirections()
    {
        var registration = new Registration();
        var controller = new StartupController(registration);
        Assert.Equal(new StartupState(false), controller.State);
        controller.Refresh();
        Assert.Empty(registration.Writes);
        controller.SetEnabled(true);
        Assert.Equal(new StartupState(true), controller.State);
        controller.SetEnabled(false);
        Assert.Equal(new StartupState(false), controller.State);
        Assert.Equal(new[] { true, false }, registration.Writes);
        registration.Current = new(true, "External warning");
        controller.Refresh();
        Assert.Equal(registration.Current, controller.State);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("permission")]
    [InlineData("security")]
    [InlineData("invalid")]
    [InlineData("argument")]
    public void ReadFailureIsVisibleAndSuccessfulRetryClearsWarning(string kind)
    {
        var registration = new Registration
        {
            ReadError = kind switch
            {
                "io" => new IOException("Test failure"),
                "permission" => new UnauthorizedAccessException("Test failure"),
                "security" => new System.Security.SecurityException("Test failure"),
                "invalid" => new InvalidOperationException("Test failure"),
                _ => new ArgumentException("Test failure")
            }
        };
        var controller = new StartupController(registration);
        controller.Refresh();
        Assert.False(controller.State.Enabled);
        Assert.Equal("Cannot read Windows startup registration: Test failure", controller.State.Warning);
        Assert.Empty(registration.Writes);
        registration.ReadError = null;
        controller.SetEnabled(true);
        Assert.True(controller.State.Enabled);
        Assert.Empty(controller.State.Warning);
    }

    [Fact]
    public void UnexpectedReadFailureIsNotSuppressed()
    {
        var controller = new StartupController(new Registration { ReadError = new NotSupportedException("Unexpected") });
        Assert.Throws<NotSupportedException>(controller.Refresh);
    }

    [Fact]
    public void WriteFailureRetainsObservedStateAndReadbackMismatchFailsExplicitly()
    {
        var registration = new Registration { WriteError = new IOException("Denied") };
        var controller = new StartupController(registration);
        Assert.Equal("Denied", Assert.Throws<IOException>(() => controller.SetEnabled(true)).Message);
        Assert.False(controller.State.Enabled);
        registration.WriteError = null;
        registration.IgnoreWrites = true;
        Assert.Contains("did not retain", Assert.Throws<IOException>(() => controller.SetEnabled(true)).Message);
        Assert.False(controller.State.Enabled);
        registration.IgnoreWrites = false;
        controller.SetEnabled(true);
        registration.WriteError = new IOException("Denied");
        Assert.Throws<IOException>(() => controller.SetEnabled(false));
        Assert.True(controller.State.Enabled);
    }

    [Fact]
    public void FailedReadbackReportsUnknownStateRatherThanSuccessfulSave()
    {
        var registration = new Registration { ReadError = new IOException("Cannot verify") };
        var controller = new StartupController(registration);
        Assert.Throws<IOException>(() => controller.SetEnabled(true));
        Assert.True(registration.Current.Enabled);
        Assert.Contains("Cannot verify", controller.State.Warning);
    }

    [Fact]
    public void RemainingRelocatedEntryCannotBeReportedAsSuccessfullyDisabled()
    {
        var registration = new Registration { Current = new(false, "Another executable is still registered"), IgnoreWrites = true };
        var controller = new StartupController(registration);
        Assert.Contains("did not retain", Assert.Throws<IOException>(() => controller.SetEnabled(false)).Message);
        Assert.Equal(registration.Current, controller.State);
    }

    [Fact]
    public void OnlyExactStartupArgumentSelectsTrayLaunch()
    {
        Assert.True(LaunchOptions.StartInTray(["--startup"]));
        Assert.False(LaunchOptions.StartInTray([]));
        Assert.False(LaunchOptions.StartInTray(["--Startup"]));
        Assert.False(LaunchOptions.StartInTray(["--startup", "unexpected"]));
        Assert.False(LaunchOptions.StartInTray(["unexpected", "--startup"]));
        Assert.False(LaunchOptions.StartInTray(["--startup-extra"]));
    }

    [Fact]
    public async Task ViewModelPersistsWithoutMovingWindowsAndWorksDuringRecovery()
    {
        var (desktop, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        var registration = new Registration();
        var controller = new StartupController(registration);
        var backend = new Hotkeys();
        using var model = new MainViewModel(coordinator,
            new ShortcutController(backend, new ShortcutStore(backend)), new Dispatcher(), startup: controller);
        Assert.False(model.StartWithWindows);
        Assert.True(model.CanChangeStartup);
        await model.ExecuteAsync(OperationKind.GatherSelected, Fixture.B);
        int moves = desktop.Moved.Count;
        Assert.True(model.HasRecovery);
        Assert.True(model.CanChangeStartup);
        await model.SaveStartupAsync(true);
        Assert.True(model.StartWithWindows);
        Assert.StartsWith("Start with Windows enabled.", model.Summary);
        Assert.False(model.HasDetails);
        Assert.Equal(moves, desktop.Moved.Count);
        registration.WriteError = new IOException("Test registration denied");
        await model.SaveStartupAsync(false);
        Assert.True(model.StartWithWindows);
        Assert.Contains("Test registration denied", model.Details);
        registration.WriteError = null;
        await model.SaveStartupAsync(false);
        Assert.False(model.StartWithWindows);
        Assert.Equal("Start with Windows disabled.", model.Summary);
        Assert.False(model.HasDetails);
        Assert.Equal(new[] { true, false }, registration.Writes);
    }

    [Fact]
    public async Task StartupWarningStaysVisibleAndUnsupportedConsumersCannotWrite()
    {
        var (_, _, engine) = Fixture.Create();
        var backend = new Hotkeys();
        var coordinator = new OperationCoordinator(engine);
        using var unsupported = new MainViewModel(coordinator,
            new ShortcutController(backend, new ShortcutStore(backend)), new Dispatcher());
        Assert.False(unsupported.CanChangeStartup);
        await unsupported.SaveStartupAsync(true);
        Assert.Contains("unavailable", unsupported.Details);
        var registration = new Registration { ReadError = new IOException("Cannot read") };
        var controller = new StartupController(registration);
        controller.Refresh();
        using var model = new MainViewModel(coordinator,
            new ShortcutController(backend, new ShortcutStore(backend)), new Dispatcher(), startup: controller);
        Assert.True(model.HasStartupWarning);
        await model.ExecuteAsync(OperationKind.Refresh);
        Assert.Contains("Cannot read", model.StartupWarning);
        registration.ReadError = null;
        await model.SaveStartupAsync(true);
        Assert.False(model.HasStartupWarning);
        Assert.Empty(model.StartupWarning);
    }

    [Fact]
    public async Task StartupUsesSharedBusyGateAndDoesNotQueueAConflictingWrite()
    {
        var (_, _, engine) = Fixture.Create();
        var coordinator = new OperationCoordinator(engine);
        var registration = new Registration();
        var backend = new Hotkeys();
        using var model = new MainViewModel(coordinator,
            new ShortcutController(backend, new ShortcutStore(backend)), new Dispatcher(),
            startup: new StartupController(registration));
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OperationReply> active = coordinator.RunAsync(() =>
        {
            started.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test was not released");
            return new(0, 0, [], "Done");
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(model.CanChangeStartup);
            await model.SaveStartupAsync(true);
            Assert.Empty(registration.Writes);
            Assert.False(model.StartWithWindows);
        }
        finally { release.Set(); }
        await active;
        Assert.True(model.CanChangeStartup);
    }

    private sealed class Registration : IStartupRegistration
    {
        public StartupState Current = new(false);
        public Exception? ReadError, WriteError;
        public bool IgnoreWrites;
        public List<bool> Writes { get; } = [];
        public StartupState Read() => ReadError is { } error ? throw error : Current;
        public void SetEnabled(bool enabled)
        {
            if (WriteError is { } error) throw error;
            Writes.Add(enabled);
            if (!IgnoreWrites) Current = new(enabled);
        }
    }
}
