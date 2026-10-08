using Xunit;

namespace WindowGather.Core.Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData(2u, 2)]
    [InlineData(6u, 2)]
    [InlineData(7u, 2)]
    [InlineData(11u, 2)]
    [InlineData(3u, 3)]
    [InlineData(0u, 1)]
    [InlineData(1u, 1)]
    [InlineData(4u, 1)]
    [InlineData(99u, 1)]
    public void NormalizesPlacementState(uint command, int state) => Assert.Equal(state, Geometry.State(command));

    [Fact]
    public void CoordinatesConvertAcrossNegativeOriginAndTopTaskbar()
    {
        var box = Geometry.FitNormal(Fixture.Place(), Fixture.A, Fixture.B);
        Assert.Equal(new Box(100, 100, 600, 500), box);
        Assert.Equal(500, box.Width);
        Assert.Equal(400, box.Height);
        Assert.Equal("DISPLAY1 - Desk (1920 x 1080)", Fixture.A.ToString());
    }

    [Theory]
    [InlineData(-4000, -4000, 4000, 4000, 0, 0, 1920, 1040)]
    [InlineData(-2000, -100, -1990, -90, 0, 0, 10, 10)]
    [InlineData(5000, 5000, 5100, 5100, 1820, 940, 1920, 1040)]
    [InlineData(-1800, 100, -1800, 100, 120, 100, 121, 101)]
    public void ClampsWindowSizeAndPosition(int left, int top, int right, int bottom,
        int x, int y, int endX, int endY)
    {
        var placement = Fixture.Place() with { Normal = new(left, top, right, bottom) };
        Assert.Equal(new Box(x, y, endX, endY), Geometry.FitNormal(placement, Fixture.A, Fixture.B));
    }

    [Theory]
    [InlineData(0u, ShortcutKey.G)]
    [InlineData(4u, ShortcutKey.G)]
    [InlineData(19u, ShortcutKey.G)]
    [InlineData(3u, ShortcutKey.ControlKey)]
    [InlineData(3u, (ShortcutKey)999)]
    public void RejectsInvalidShortcuts(uint modifiers, ShortcutKey key) =>
        Assert.Throws<InvalidDataException>(() => new Shortcut(modifiers, key).Validate());

    [Theory]
    [InlineData(1u, ShortcutKey.A, "Alt + A")]
    [InlineData(2u, ShortcutKey.Z, "Ctrl + Z")]
    [InlineData(8u, ShortcutKey.F24, "Win + F24")]
    [InlineData(15u, ShortcutKey.D7, "Ctrl + Alt + Shift + Win + 7")]
    [InlineData(3u, ShortcutKey.D0, "Ctrl + Alt + 0")]
    [InlineData(3u, ShortcutKey.D9, "Ctrl + Alt + 9")]
    public void FormatsAndValidatesShortcuts(uint modifiers, ShortcutKey key, string label)
    {
        var shortcut = new Shortcut(modifiers, key);
        shortcut.Validate();
        Assert.Equal(label, shortcut.ToString());
    }

    [Fact]
    public void DefaultsPreserveLegacyNumericValuesAndDistinctness()
    {
        var settings = ShortcutSettings.Defaults;
        settings.Validate();
        Assert.Equal(122, (int)settings.Gather.Key);
        Assert.Equal(123, (int)settings.Restore.Key);
        Assert.Equal(3u, settings.Gather.Modifiers);
        Assert.Equal(60, Shortcut.SupportedKeys.Count);
        Assert.Throws<InvalidDataException>(() => new ShortcutSettings(settings.Gather, settings.Gather).Validate());
        Assert.Throws<InvalidDataException>(() => new ShortcutSettings(null!, settings.Restore).Validate());
        Assert.Throws<InvalidDataException>(() => new ShortcutSettings(settings.Gather, null!).Validate());
    }

    [Fact]
    public void CoordinatesIncludeOffsetWorkAreasOnBothDisplays()
    {
        var origin = Fixture.A with
        {
            Bounds = new(-1600, -900, 0, 0), WorkArea = new(-1550, -860, 0, 0)
        };
        var target = Fixture.B with
        {
            Bounds = new(100, -1200, 2020, -120), WorkArea = new(160, -1160, 2020, -160)
        };
        var placement = Fixture.Place() with { Normal = new(-1500, -800, -1000, -400) };
        Box result = Geometry.FitNormal(placement, origin, target);
        Assert.Equal(new Box(220, -1084, 720, -684), result);
        var point = new Position(12, -90);
        var (x, y) = point;
        Assert.Equal(12, x);
        Assert.Equal(-90, y);
        Assert.Equal("Position { X = 12, Y = -90 }", point.ToString());
    }

    [Fact]
    public void SettingsValidateBothActionsAndExplainFailures()
    {
        var invalid = new Shortcut(0, ShortcutKey.G);
        Assert.Contains("Choose Ctrl", Assert.Throws<InvalidDataException>(invalid.Validate).Message);
        Assert.Contains("Both shortcuts", Assert.Throws<InvalidDataException>(() =>
            new ShortcutSettings(null!, ShortcutSettings.Defaults.Restore).Validate()).Message);
        Assert.Throws<InvalidDataException>(() => new ShortcutSettings(invalid, ShortcutSettings.Defaults.Restore).Validate());
        Assert.Throws<InvalidDataException>(() => new ShortcutSettings(ShortcutSettings.Defaults.Gather, invalid).Validate());
        Assert.Contains("different shortcuts", Assert.Throws<InvalidDataException>(() =>
            new ShortcutSettings(ShortcutSettings.Defaults.Gather, ShortcutSettings.Defaults.Gather).Validate()).Message);
    }
}
