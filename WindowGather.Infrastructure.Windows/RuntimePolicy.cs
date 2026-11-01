using System.Security.Principal;

namespace WindowGather;

public static class RuntimePolicy
{
    public static void RequireStandardUser()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        ValidateElevation(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) || identity.IsSystem);
    }

    internal static void ValidateElevation(bool elevated)
    {
        if (elevated)
            throw new InvalidOperationException("Window Gather supports standard-user execution only. " +
                "Close it and launch normally, not as administrator. Protected applications may refuse movement.");
    }
}
