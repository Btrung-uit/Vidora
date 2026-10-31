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
        var fullPath = Path.GetFullPath(path);
        using var handle = CreateFile(
            fullPath,
            0,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new IOException(
                $"Could not resolve canonical path '{fullPath}'.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0)
        {
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
                throw new IOException($"Canonical path is too long for '{fullPath}'.");
            }
        }

        return RemoveDevicePrefix(buffer.ToString());
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
}
