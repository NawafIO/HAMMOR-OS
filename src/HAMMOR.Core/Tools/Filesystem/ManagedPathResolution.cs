using System.IO;

namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Portable fallback that does not call Windows-only APIs. Used on non-Windows
/// hosts and as the default when Platform.Windows is not wired.
/// </summary>
public sealed class ManagedPathResolution : IPathResolution
{
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
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return null;
            }

            // Cannot reliably follow junctions without P/Invoke — signal
            // unresolvable so the policy denies safely (fail-closed).
            if (IsReparsePoint(path))
            {
                return null;
            }

            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);
}
