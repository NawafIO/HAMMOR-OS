using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using HAMMOR.Core.Tools.Filesystem;
using Microsoft.Win32.SafeHandles;

namespace HAMMOR.Platform.Windows.Filesystem;

/// <summary>
/// Windows-specific path helpers using real reparse-point detection (P/Invoke).
/// </summary>
public sealed class WindowsPathResolution : IPathResolution
{
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        [Out] char[] lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    public bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    public string? ResolveFinalPath(string path)
    {
        // If not a reparse, return normalized path.
        if (!Exists(path))
        {
            return null;
        }

        try
        {
            // Try reparse-aware open first (to inspect the link itself), then fall back
            // to following the link to its final target.
            var resolved = TryResolve(path, followLink: true);
            if (resolved is not null)
            {
                return resolved;
            }

            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);

    private static bool Exists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static string? TryResolve(string path, bool followLink)
    {
        var flags = FILE_FLAG_BACKUP_SEMANTICS;
        if (!followLink)
        {
            flags |= FILE_FLAG_OPEN_REPARSE_POINT;
        }

        using var handle = CreateFileW(
            path,
            0,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            flags,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            // If we cannot open the reparse, let the caller deny safely.
            if (error != 0)
            {
                return null;
            }

            return null;
        }

        const int capacity = 32767;
        var buffer = new char[capacity];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length >= capacity)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                return null;
            }

            return null;
        }

        var raw = new string(buffer, 0, (int)length);

        // GetFinalPathNameByHandle returns \\?\... — strip the prefix.
        if (raw.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            raw = raw[4..];
            if (raw.StartsWith(@"UNC\", StringComparison.Ordinal))
            {
                raw = @"\\" + raw[4..];
            }
        }

        return raw;
    }
}
