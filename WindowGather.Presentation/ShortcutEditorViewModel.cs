using CommunityToolkit.Mvvm.ComponentModel;

namespace WindowGather.Presentation;

public sealed partial class ShortcutEditorViewModel : ObservableObject
{
    [ObservableProperty] public partial bool Ctrl { get; set; }
    [ObservableProperty] public partial bool Alt { get; set; }
    [ObservableProperty] public partial bool Shift { get; set; }
    [ObservableProperty] public partial bool Win { get; set; }
    [ObservableProperty] public partial ShortcutKey Key { get; set; }
    public IReadOnlyList<ShortcutKey> Keys => Shortcut.SupportedKeys;

    public ShortcutEditorViewModel(Shortcut shortcut) => SetShortcut(shortcut);

    public void SetShortcut(Shortcut shortcut)
    {
        Ctrl = (shortcut.Modifiers & 2) != 0;
        Alt = (shortcut.Modifiers & 1) != 0;
        Shift = (shortcut.Modifiers & 4) != 0;
        Win = (shortcut.Modifiers & 8) != 0;
        Key = shortcut.Key;
    }

    public Shortcut GetShortcut() => new(
        (Ctrl ? 2u : 0) | (Alt ? 1u : 0) | (Shift ? 4u : 0) | (Win ? 8u : 0), Key);
}
