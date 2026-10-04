namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Windows-specific path helpers hidden behind an abstraction so Core stays
/// free of <c>net8.0-windows</c>. Platform.Windows provides the real
/// implementation; tests and non-Windows hosts can use a stub.
/// </summary>
public interface IPathResolution
{
    /// <summary>
    /// Returns true when <paramref name="path"/> designates a reparse point
    /// (symlink, junction, mount point).
    /// </summary>
    bool IsReparsePoint(string path);

    /// <summary>
    /// Resolves the ultimate target of <paramref name="path"/> (follows
    /// reparse chains). When <paramref name="path"/> is not a reparse point,
    /// returns <paramref name="path"/> unchanged. Returns null when the
    /// target cannot be resolved.
    /// </summary>
    string? ResolveFinalPath(string path);

    /// <summary>Canonicalises <paramref name="path"/> for comparison.</summary>
    string GetFullPath(string path);
}
