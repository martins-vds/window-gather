namespace WindowGather;

public enum ShortcutKey
{
    ControlKey = 17,
    D0 = 48, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24
}

public sealed record Shortcut(uint Modifiers, ShortcutKey Key)
{
    public static IReadOnlyList<ShortcutKey> SupportedKeys { get; } = Array.AsReadOnly(
        Enumerable.Range((int)ShortcutKey.A, 26).Concat(Enumerable.Range((int)ShortcutKey.D0, 10))
            .Concat(Enumerable.Range((int)ShortcutKey.F1, 24)).Select(k => (ShortcutKey)k).ToArray());

    public void Validate()
    {
        if ((Modifiers & ~15u) != 0 || (Modifiers & 11u) == 0 || !SupportedKeys.Contains(Key))
            throw new InvalidDataException("Choose Ctrl, Alt, or Win (optionally Shift), and a letter, number, or function key.");
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        if ((Modifiers & 8) != 0) parts.Add("Win");
        parts.Add(Key >= ShortcutKey.D0 && Key <= ShortcutKey.D9 ? ((int)Key - (int)ShortcutKey.D0).ToString() : Key.ToString());
        return string.Join(" + ", parts);
    }
}

public sealed record ShortcutSettings(Shortcut Gather, Shortcut Restore)
{
    public static ShortcutSettings Defaults => new(new(3, ShortcutKey.F11), new(3, ShortcutKey.F12));

    public void Validate()
    {
        if (Gather is null || Restore is null)
            throw new InvalidDataException("Both shortcuts must be specified.");
        Gather.Validate();
        Restore.Validate();
        if (Gather == Restore) throw new InvalidDataException("Gather and Restore must use different shortcuts.");
    }
}
