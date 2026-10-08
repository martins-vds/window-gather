using System.ComponentModel;
using Xunit;

namespace WindowGather.Core.Tests;

public sealed class ShortcutTests
{
    private static readonly ShortcutSettings Custom = new(new(6, ShortcutKey.G), new(9, ShortcutKey.F24));

    [Fact]
    public void ReplacementsReserveThenSaveThenRelease()
    {
        var backend = new Hotkeys();
        var store = new ShortcutStore(backend);
        var controller = new ShortcutController(backend, store);
        Assert.Empty(controller.Activate(ShortcutSettings.Defaults));
        Assert.NotNull(controller.GatherId);
        Assert.NotNull(controller.RestoreId);
        backend.Log.Clear();
        controller.Apply(Custom);
        Assert.Equal(new[] { "register", "register", "save", "release", "release" }, backend.Log);
        Assert.Equal(Custom, controller.Settings);
        Assert.Equal(Custom, store.Load());
        Assert.All(backend.Registered.Keys, id => Assert.True(id > 0));
        controller.Apply(new(Custom.Restore, Custom.Gather));
        Assert.Equal(2, backend.Registered.Count);
        controller.Release();
        Assert.Empty(backend.Registered);
        Assert.Null(controller.GatherId);
        Assert.Null(controller.RestoreId);
    }

    [Theory]
    [InlineData("first")]
    [InlineData("second")]
    [InlineData("disk")]
    public void PrecommitFailurePreservesOldMappingsAndRegistrations(string failure)
    {
        var backend = new Hotkeys();
        var store = new ShortcutStore(backend);
        var controller = new ShortcutController(backend, store);
        controller.Activate(ShortcutSettings.Defaults);
        int? gatherId = controller.GatherId;
        int? restoreId = controller.RestoreId;
        if (failure == "disk") store.FailWrites = true;
        else backend.Blocked = failure == "first" ? Custom.Gather : Custom.Restore;
        if (failure == "disk") Assert.Throws<IOException>(() => controller.Apply(Custom));
        else Assert.Throws<Win32Exception>(() => controller.Apply(Custom));
        Assert.Equal(ShortcutSettings.Defaults, controller.Settings);
        Assert.Equal(ShortcutSettings.Defaults, store.Current);
        Assert.Equal(gatherId, controller.GatherId);
        Assert.Equal(restoreId, controller.RestoreId);
        Assert.Equal(2, backend.Registered.Count);
    }

    [Fact]
    public void StartupConflictIsVisibleAndRepairable()
    {
        var backend = new Hotkeys { Blocked = ShortcutSettings.Defaults.Gather };
        var controller = new ShortcutController(backend, new ShortcutStore(backend));
        Assert.Single(controller.Activate(ShortcutSettings.Defaults));
        Assert.Null(controller.GatherId);
        Assert.NotNull(controller.RestoreId);
        Assert.Single(controller.Activate(ShortcutSettings.Defaults));
        Assert.Single(backend.Registered);
        controller.Apply(Custom);
        Assert.Equal(2, backend.Registered.Count);
    }

    [Fact]
    public void InvalidSettingsCannotReachBackendAndActivationUsesGivenMappings()
    {
        var backend = new Hotkeys();
        var store = new ShortcutStore(backend);
        var controller = new ShortcutController(backend, store);
        var invalid = new ShortcutSettings(new(0, ShortcutKey.G), Custom.Restore);
        Assert.Throws<InvalidDataException>(() => controller.Activate(invalid));
        Assert.Throws<InvalidDataException>(() => controller.Apply(invalid));
        Assert.Empty(backend.Registered);
        Assert.Empty(controller.Activate(Custom));
        Assert.Equal(Custom, controller.Settings);
        controller.Apply(Custom);
        Assert.Equal(2, backend.Registered.Count);
    }
}
