using WindowGather;

internal static class StartupTests
{
    internal static void RunAll(Action<string, Action> run)
    {
        const string exe = @"C:\Program Files\Window Gather\WindowGather.WinUI.exe";
        const string command = "\"C:\\Program Files\\Window Gather\\WindowGather.WinUI.exe\" --startup";
        run("startup defaults off without creating a registration", () =>
        {
            var entry = new Entry();
            var registration = new WindowsStartupRegistration(exe, entry);
            Check(!registration.Read().Enabled && registration.Read().Warning == "");
            registration.SetEnabled(false);
            Check(entry.Writes == 0 && entry.Deletes == 0);
        });
        run("startup quotes outer executable and registers only the tray argument", () =>
        {
            var entry = new Entry();
            var registration = new WindowsStartupRegistration(exe, entry);
            registration.SetEnabled(true);
            Check(Equals(entry.Value, command) && registration.Read() == new StartupState(true));
            registration.SetEnabled(true);
            Check(entry.Writes == 1);
            registration.SetEnabled(false);
            Check(entry.Value is null && entry.Deletes == 1 && !registration.Read().Enabled);
        });
        run("startup path case does not create duplicate writes", () =>
        {
            var entry = new Entry { Value = command.ToUpperInvariant().Replace("--STARTUP", "--startup") };
            var registration = new WindowsStartupRegistration(exe, entry);
            Check(registration.Read().Enabled);
            registration.SetEnabled(true);
            Check(entry.Writes == 0);
        });
        run("startup detects a relocated executable and repairs only on explicit enable", () =>
        {
            var entry = new Entry { Value = "\"D:\\Old\\WindowGather.WinUI.exe\" --startup" };
            var registration = new WindowsStartupRegistration(exe, entry);
            Check(!registration.Read().Enabled && registration.Read().Warning.Contains("another"));
            Check(entry.Writes == 0);
            registration.SetEnabled(true);
            Check(Equals(entry.Value, command) && registration.Read().Warning == "");
            entry.Value = "\"D:\\Old\\WindowGather.WinUI.exe\" --startup";
            registration.SetEnabled(false);
            Check(entry.Value is null);
        });
        foreach (object value in new object[] { 42, new byte[] { 1 }, "", "\"C:\\Other.exe\" --startup", command.Replace("--startup", "--STARTUP"),
            "\"WindowGather.WinUI.exe\" --startup", "\"C:\\Old\\WindowGather.WinUI.exe\" --startup extra",
            "\"C:\\Bad\"Quote\\WindowGather.WinUI.exe\" --startup",
            "\"C:\\Bad\nPath\\WindowGather.WinUI.exe\" --startup" })
            run("unrecognized startup entries cannot be silently overwritten or deleted: " + value.GetType().Name, () =>
            {
                var entry = new Entry { Value = value };
                var registration = new WindowsStartupRegistration(exe, entry);
                Throws<InvalidOperationException>(() => registration.Read());
                Throws<InvalidOperationException>(() => registration.SetEnabled(true));
                Throws<InvalidOperationException>(() => registration.SetEnabled(false));
                Check(entry.Writes == 0 && entry.Deletes == 0 && Equals(entry.Value, value));
            });
        run("startup refuses commands longer than the documented Windows limit", () =>
        {
            var entry = new Entry();
            string path = "C:\\" + new string('a', 230) + "\\WindowGather.WinUI.exe";
            var registration = new WindowsStartupRegistration(path, entry);
            Throws<InvalidOperationException>(() => registration.SetEnabled(true));
            Check(entry.Writes == 0);
            int directoryLength = 260 - "C:\\".Length - "\\WindowGather.WinUI.exe".Length - "\"\" --startup".Length;
            path = "C:\\" + new string('a', directoryLength) + "\\WindowGather.WinUI.exe";
            Check($"\"{path}\" --startup".Length == 260);
            new WindowsStartupRegistration(path, entry).SetEnabled(true);
            Check(entry.Writes == 1);
        });
        foreach (string path in new[] { "WindowGather.WinUI.exe", @"C:\App.dll", "C:\\Bad\"Name.exe", "C:\\Bad\nName.exe" })
            run("startup rejects invalid executable path without touching registry", () =>
                Throws<ArgumentException>(() => new WindowsStartupRegistration(path, new Entry())));
        run("startup backend failures are not hidden", () =>
        {
            var entry = new Entry { Fail = true };
            var registration = new WindowsStartupRegistration(exe, entry);
            Throws<UnauthorizedAccessException>(() => registration.Read());
            Throws<UnauthorizedAccessException>(() => registration.SetEnabled(true));
        });
        run("failed startup writes and deletes preserve the existing entry", () =>
        {
            var entry = new Entry { FailWrite = true };
            var registration = new WindowsStartupRegistration(exe, entry);
            Throws<IOException>(() => registration.SetEnabled(true));
            Check(entry.Value is null);
            entry.FailWrite = false;
            registration.SetEnabled(true);
            entry.FailWrite = true;
            Throws<IOException>(() => registration.SetEnabled(false));
            Check(Equals(entry.Value, command) && registration.Read().Enabled);
        });
        run("startup registry scope and value are per-user and app-specific", () =>
        {
            Check(WindowsStartupRegistration.KeyPath == @"Software\Microsoft\Windows\CurrentVersion\Run");
            Check(WindowsStartupRegistration.ValueName == "WindowGather");
        });
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Startup assertion failed.");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class Entry : IStartupEntry
    {
        internal object? Value;
        internal int Writes, Deletes;
        internal bool Fail, FailWrite;
        public object? Read() => Fail ? throw new UnauthorizedAccessException("Test registry access denied") : Value;
        public void Write(string command)
        {
            if (FailWrite) throw new IOException("Test startup write denied");
            Value = command;
            Writes++;
        }
        public void Delete()
        {
            if (FailWrite) throw new IOException("Test startup delete denied");
            Value = null;
            Deletes++;
        }
    }
}
