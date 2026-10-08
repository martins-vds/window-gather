using System.ComponentModel;
using System.Text.Json;

namespace WindowGather;

public sealed class ShortcutStore(string directory) : IShortcutStore
{
    private readonly string path = Path.Combine(directory, "shortcuts.json");

    public ShortcutSettings Load()
    {
        if (!File.Exists(path)) return ShortcutSettings.Defaults;
        if (new FileInfo(path).Length > 4096)
            throw new InvalidDataException("The shortcut settings file is too large.");
        var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The shortcut settings file is empty.");
        if (data.Gather is null || data.Restore is null)
            throw new InvalidDataException("Both shortcuts must be specified.");
        var settings = new ShortcutSettings(data.Gather.ToShortcut(), data.Restore.ToShortcut());
        settings.Validate();
        return settings;
    }

    public void Save(ShortcutSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        var data = new SettingsData(ShortcutData.From(settings.Gather), ShortcutData.From(settings.Restore));
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(JsonSerializer.SerializeToUtf8Bytes(data));
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record ShortcutData(uint Modifiers, int Key)
    {
        public Shortcut ToShortcut() => new(Modifiers, (ShortcutKey)Key);
        public static ShortcutData From(Shortcut shortcut) => new(shortcut.Modifiers, (int)shortcut.Key);
    }

    private sealed record SettingsData(ShortcutData Gather, ShortcutData Restore);
}

public sealed class NativeHotkeyBackend(Func<nint> handle) : IHotkeyBackend
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
