using System.ComponentModel;

namespace WindowGather;

public interface IShortcutStore
{
    ShortcutSettings Load();
    void Save(ShortcutSettings settings);
}

public interface IHotkeyBackend
{
    void Register(int id, Shortcut shortcut);
    void Unregister(int id);
}

// Keep existing registrations until both replacement shortcuts are reserved and saved.
public sealed class ShortcutController(IHotkeyBackend backend, IShortcutStore store)
{
    private readonly Dictionary<int, Shortcut> registrations = [];
    private int nextId;
    public ShortcutSettings Settings { get; private set; } = ShortcutSettings.Defaults;
    public int? GatherId => FindId(Settings.Gather);
    public int? RestoreId => FindId(Settings.Restore);
    private int? FindId(Shortcut shortcut) =>
        registrations.Where(r => r.Value == shortcut).Select(r => (int?)r.Key).FirstOrDefault();

    public IReadOnlyList<string> Activate(ShortcutSettings settings)
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

    public void Apply(ShortcutSettings settings)
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

    public void Release()
    {
        foreach (int id in registrations.Keys.ToArray())
        {
            backend.Unregister(id);
            registrations.Remove(id);
        }
    }
}
