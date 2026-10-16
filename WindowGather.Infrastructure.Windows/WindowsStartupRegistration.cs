using Microsoft.Win32;

namespace WindowGather;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    internal const string ValueName = "WindowGather";
    internal const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly IStartupEntry entry;
    private readonly string command;

    public WindowsStartupRegistration() : this(Environment.ProcessPath ??
        throw new InvalidOperationException("Cannot locate the running executable."), new RegistryStartupEntry()) { }

    internal WindowsStartupRegistration(string executablePath, IStartupEntry entry)
    {
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.IndexOfAny(['"', '\r', '\n', '\0']) >= 0 ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Startup requires an absolute executable path.", nameof(executablePath));
        this.entry = entry;
        command = $"\"{executablePath}\" {LaunchOptions.StartupArgument}";
    }

    public StartupState Read()
    {
        string? saved = ReadOwnedEntry();
        if (saved is null) return new(false);
        if (MatchesCurrentCommand(saved)) return new(true);
        return new(false, "Startup points to another Window Gather location. Enable this option to use this executable, or disable it to remove the old entry.");
    }

    public void SetEnabled(bool enabled)
    {
        string? saved = ReadOwnedEntry();
        if (enabled)
        {
            if (command.Length > 260)
                throw new InvalidOperationException("The startup command exceeds Windows' 260-character limit. Move Window Gather to a shorter permanent path and retry.");
            if (!MatchesCurrentCommand(saved)) entry.Write(command);
        }
        else if (saved is not null) entry.Delete();
    }

    private string? ReadOwnedEntry()
    {
        object? value = entry.Read();
        if (value is null) return null;
        if (value is string saved && IsOwnedCommand(saved)) return saved;
        throw new InvalidOperationException("The WindowGather startup entry contains an unrecognized command. Inspect it in Windows before replacing it; no entry was changed.");
    }

    private bool IsOwnedCommand(string value)
    {
        if (MatchesCurrentCommand(value)) return true;
        string suffix = $"\" {LaunchOptions.StartupArgument}";
        if (!value.StartsWith('"') || !value.EndsWith(suffix, StringComparison.Ordinal)) return false;
        string path = value[1..^suffix.Length];
        return path.Length > 0 && path.IndexOfAny(['"', '\r', '\n', '\0']) < 0 &&
            Path.IsPathFullyQualified(path) &&
            string.Equals(Path.GetFileName(path), "WindowGather.WinUI.exe", StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesCurrentCommand(string? value) =>
        value is not null && value.EndsWith(LaunchOptions.StartupArgument, StringComparison.Ordinal) &&
        string.Equals(value, command, StringComparison.OrdinalIgnoreCase);
}

internal interface IStartupEntry
{
    object? Read();
    void Write(string command);
    void Delete();
}

internal sealed class RegistryStartupEntry : IStartupEntry
{
    public object? Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(WindowsStartupRegistration.KeyPath);
        object? value = key?.GetValue(WindowsStartupRegistration.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (key is not null && value is not null &&
            key.GetValueKind(WindowsStartupRegistration.ValueName) != RegistryValueKind.String)
            throw new InvalidOperationException("The WindowGather startup entry is not a plain command string. No entry was changed.");
        return value;
    }

    public void Write(string command)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(WindowsStartupRegistration.KeyPath, writable: true)
            ?? throw new IOException("Cannot open the current user's Windows startup key.");
        key.SetValue(WindowsStartupRegistration.ValueName, command, RegistryValueKind.String);
        key.Flush();
    }

    public void Delete()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(WindowsStartupRegistration.KeyPath, writable: true);
        if (key is null) return;
        key.DeleteValue(WindowsStartupRegistration.ValueName, throwOnMissingValue: false);
        key.Flush();
    }
}
