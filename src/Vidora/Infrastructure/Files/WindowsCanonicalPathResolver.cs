using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Vidora.Infrastructure.Files;

public sealed class WindowsCanonicalPathResolver : ICanonicalPathResolver
{
    private const uint FileFlagBackupSemantics = 0x02000000;

    public string Resolve(string path)
    {
        using var lease = Acquire(path);
        return lease.Path;
    }

    public ICanonicalPathLease Acquire(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var handle = CreateFile(
            fullPath,
            0,
            FileShare.ReadWrite,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new IOException(
                $"Could not resolve canonical path '{fullPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0)
        {
            handle.Dispose();
            throw new IOException(
                $"Could not resolve canonical path '{fullPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        if (length >= buffer.Capacity)
        {
            buffer.EnsureCapacity(checked((int)length + 1));
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity)
            {
                handle.Dispose();
                throw new IOException($"Canonical path is too long for '{fullPath}'.");
            }
        }

        return new CanonicalPathLease(RemoveDevicePrefix(buffer.ToString()), handle);
    }

    private static string RemoveDevicePrefix(string path) => path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
        ? @"\\" + path[8..]
        : path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            ? path[4..]
            : path;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);

    private sealed class CanonicalPathLease(string path, SafeFileHandle handle) : ICanonicalPathLease
    {
        public string Path { get; } = path;

        public void Dispose() => handle.Dispose();
    }
}
