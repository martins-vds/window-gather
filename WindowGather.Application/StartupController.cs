namespace WindowGather;

public sealed record StartupState(bool Enabled, string Warning = "");

public interface IStartupRegistration
{
    StartupState Read();
    void SetEnabled(bool enabled);
}

public sealed class StartupController(IStartupRegistration registration)
{
    public StartupState State { get; private set; } = new(false);

    public void Refresh()
    {
        try { State = registration.Read(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or InvalidOperationException or ArgumentException)
        {
            State = new(false, $"Cannot read Windows startup registration: {error.Message}");
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            registration.SetEnabled(enabled);
            State = registration.Read();
            if (State.Enabled != enabled || State.Warning.Length > 0)
                throw new IOException("Windows startup registration did not retain the requested setting. Retry after checking Windows Startup apps.");
        }
        catch
        {
            Refresh();
            throw;
        }
    }
}

public static class LaunchOptions
{
    public const string StartupArgument = "--startup";
    public static bool StartInTray(IEnumerable<string> arguments) =>
        arguments.SequenceEqual([StartupArgument], StringComparer.Ordinal);
}
