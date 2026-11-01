using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace WindowGather;

internal static class LocalStateFile
{
    public static string? Read(string path, int maximumBytes)
    {
        if (!Exists(path)) return null;
        ProtectDirectory(Path.GetDirectoryName(path)!);
        RejectLink(path);
        ProtectFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException($"The settings/recovery file is too large: {path}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static void Write(string path, byte[] bytes)
    {
        ProtectDirectory(Path.GetDirectoryName(path)!);
        if (Exists(path)) RejectLink(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // CreateNew cannot truncate an existing file or follow a pre-created link.
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            try
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            catch
            {
                stream.Dispose();
                File.Delete(temporary);
                throw;
            }
        }
        try { File.Move(temporary, path, overwrite: true); }
        finally { File.Delete(temporary); }
    }

    public static void Delete(string path)
    {
        if (!Exists(path)) return;
        ProtectDirectory(Path.GetDirectoryName(path)!);
        RejectLink(path);
        File.Delete(path);
    }

    private static void ProtectDirectory(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Settings/recovery cannot use a linked directory: {current.FullName}");
        DirectoryInfo info = Directory.CreateDirectory(directory);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new UnauthorizedAccessException("Cannot identify the current Windows user.");
        DirectorySecurity existing = info.GetAccessControl(AccessControlSections.Owner);
        if (!user.Equals(existing.GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException($"Settings/recovery directory is not owned by the current user: {directory}");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (SecurityIdentifier principal in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private static void ProtectFile(string path)
    {
        var info = new FileInfo(path);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new UnauthorizedAccessException("Cannot identify the current Windows user.");
        if (!user.Equals(info.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException($"Settings/recovery file is not owned by the current user: {path}");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (SecurityIdentifier principal in new[] { user, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Settings/recovery cannot use a linked file: {path}");
    }

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
