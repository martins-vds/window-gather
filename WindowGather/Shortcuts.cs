using System.ComponentModel;
using System.Text.Json;

namespace WindowGather;

internal sealed record Shortcut(uint Modifiers, Keys Key)
{
    internal static readonly Keys[] SupportedKeys =
        Enumerable.Range((int)Keys.A, 26).Concat(Enumerable.Range((int)Keys.D0, 10))
            .Concat(Enumerable.Range((int)Keys.F1, 24)).Select(k => (Keys)k).ToArray();

    internal void Validate()
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
        parts.Add(Key >= Keys.D0 && Key <= Keys.D9 ? ((int)Key - (int)Keys.D0).ToString() : Key.ToString());
        return string.Join(" + ", parts);
    }
}

internal sealed record ShortcutSettings(Shortcut Gather, Shortcut Restore)
{
    internal static ShortcutSettings Defaults => new(new(3, Keys.F11), new(3, Keys.F12));

    internal void Validate()
    {
        if (Gather is null || Restore is null)
            throw new InvalidDataException("Both shortcuts must be specified.");
        Gather.Validate();
        Restore.Validate();
        if (Gather == Restore) throw new InvalidDataException("Gather and Restore must use different shortcuts.");
    }
}

internal interface IShortcutStore
{
    ShortcutSettings Load();
    void Save(ShortcutSettings settings);
}

internal sealed class ShortcutStore(string directory) : IShortcutStore
{
    private readonly string path = Path.Combine(directory, "shortcuts.json");

    public ShortcutSettings Load()
    {
        if (!File.Exists(path)) return ShortcutSettings.Defaults;
        if (new FileInfo(path).Length > 4096)
            throw new InvalidDataException("The shortcut settings file is too large.");
        var settings = JsonSerializer.Deserialize<ShortcutSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The shortcut settings file is empty.");
        settings.Validate();
        return settings;
    }

    public void Save(ShortcutSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(JsonSerializer.SerializeToUtf8Bytes(settings));
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}

internal interface IHotkeyBackend
{
    void Register(int id, Shortcut shortcut);
    void Unregister(int id);
}

internal sealed class NativeHotkeyBackend(Func<nint> handle) : IHotkeyBackend
{
    public void Register(int id, Shortcut shortcut)
    {
        if (!Native.RegisterHotKey(handle(), id, shortcut.Modifiers | 0x4000, (uint)shortcut.Key))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                $"Cannot register {shortcut}: " +
                new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message);
    }

    public void Unregister(int id)
    {
        if (!Native.UnregisterHotKey(handle(), id))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "Cannot release a registered shortcut.");
    }
}

// Keep existing registrations until both replacement shortcuts are reserved and saved.
internal sealed class ShortcutController(IHotkeyBackend backend, IShortcutStore store)
{
    private readonly Dictionary<int, Shortcut> registrations = [];
    private int nextId;
    internal ShortcutSettings Settings { get; private set; } = ShortcutSettings.Defaults;
    internal int? GatherId => FindId(Settings.Gather);
    internal int? RestoreId => FindId(Settings.Restore);
    private int? FindId(Shortcut shortcut) =>
        registrations.Where(r => r.Value == shortcut).Select(r => (int?)r.Key).FirstOrDefault();

    internal IReadOnlyList<string> Activate(ShortcutSettings settings)
    {
        settings.Validate();
        Settings = settings;
        var errors = new List<string>();
        foreach (Shortcut shortcut in new[] { settings.Gather, settings.Restore })
        {
            try { Register(shortcut); }
            catch (Win32Exception error) { errors.Add(error.Message); }
        }
        return errors;
    }

    private void Register(Shortcut shortcut)
    {
        if (FindId(shortcut) is not null) return;
        int id = ++nextId;
        backend.Register(id, shortcut);
        registrations.Add(id, shortcut);
    }

    internal void Apply(ShortcutSettings settings)
    {
        settings.Validate();
        int[] previousIds = registrations.Keys.ToArray();
        try
        {
            Register(settings.Gather);
            Register(settings.Restore);
            store.Save(settings);
        }
        catch
        {
            foreach (int id in registrations.Keys.Except(previousIds).ToArray())
            {
                backend.Unregister(id);
                registrations.Remove(id);
            }
            throw;
        }
        Settings = settings;
        foreach (int id in registrations.Where(r => r.Value != settings.Gather && r.Value != settings.Restore)
            .Select(r => r.Key).ToArray())
        {
            backend.Unregister(id);
            registrations.Remove(id);
        }
    }

    internal void Release()
    {
        foreach (int id in registrations.Keys.ToArray())
        {
            backend.Unregister(id);
            registrations.Remove(id);
        }
    }
}
