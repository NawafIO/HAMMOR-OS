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
        // Portable fallback cannot follow junctions reliably without P/Invoke.
        // Callers treat null as "unresolvable" and deny the operation.
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);
}
