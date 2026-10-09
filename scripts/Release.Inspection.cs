using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowGather
{
    public static class ReleaseInspection
    {
        public static Dictionary<string, string> AssemblyMetadata(string path)
        {
            using var input = File.OpenRead(path);
            using var pe = new PEReader(input);
            var reader = pe.GetMetadataReader();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
                var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                if (reader.GetString(type.Namespace) != "System.Reflection" ||
                    reader.GetString(type.Name) != "AssemblyMetadataAttribute") continue;
                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) throw new InvalidDataException("Invalid metadata attribute.");
                values.Add(blob.ReadSerializedString(), blob.ReadSerializedString());
            }
            return values;
        }

        public static int Machine(string path)
        {
            using var input = File.OpenRead(path);
            using var pe = new PEReader(input);
            return (int)pe.PEHeaders.CoffHeader.Machine;
        }

        public static string NativeManifest(string path)
        {
            // Data/resource-only mapping never executes the app, including a foreign-architecture PE.
            var module = LoadLibraryEx(Path.GetFullPath(path), IntPtr.Zero, 0x60);
            if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var resource = FindResource(module, new IntPtr(1), new IntPtr(24));
                if (resource == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                var size = SizeofResource(module, resource);
                var pointer = LockResource(LoadResource(module, resource));
                if (size == 0 || pointer == IntPtr.Zero) throw new InvalidDataException("Empty native manifest.");
                var bytes = new byte[checked((int)size)];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').TrimEnd('\0');
            }
            finally { FreeLibrary(module); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    }
}
