using System.IO;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Storage;

namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Default centralized filesystem policy. Approved roots come from existing
/// configuration: <see cref="HammorPaths.DataRoot"/> (always allowed),
/// any explicit <see cref="HammorProject.RootPath"/> (when present), and
/// additional allow-listed roots from <see cref="SecuritySettings"/>.
/// </summary>
/// <remarks>
/// Rejects:
/// - blank/invalid/overlong paths
/// - traversal and normalization escapes
/// - paths outside approved roots
/// - protected Windows/system locations
/// - HAMMOR secrets directory
/// - reparse/symlink paths that escape or cannot be safely resolved
/// </remarks>
public sealed class FilesystemPolicy : IFilesystemPolicy
{
    private readonly List<string> _allowedRoots;
    private readonly string _secretsDirectory;
    private readonly string _dataRoot;
    private readonly IPathResolution _pathResolution;

    public FilesystemPolicy(
        IConfigurationStore configurationStore,
        HammorPaths hammorPaths,
        IPathResolution? pathResolution = null)
    {
        ArgumentNullException.ThrowIfNull(configurationStore);
        ArgumentNullException.ThrowIfNull(hammorPaths);

        _pathResolution = pathResolution ?? new ManagedPathResolution();
        _dataRoot = EnsureTrailingSeparator(hammorPaths.DataRoot);
        _secretsDirectory = EnsureTrailingSeparator(hammorPaths.SecretsDirectory);

        _allowedRoots = new List<string> { _dataRoot };

        // Memory.RootPath: when the user configures a custom memory location,
        // that location is also an approved root.
        var memoryRoot = configurationStore.Current.Memory.RootPath;
        if (!string.IsNullOrWhiteSpace(memoryRoot))
        {
            TryAddRoot(memoryRoot);
        }

        // SecuritySettings.FilesystemAllowedRoots: optional explicit allow-list.
        var extraRoots = configurationStore.Current.Security.FilesystemAllowedRoots;
        if (extraRoots is not null)
        {
            foreach (var extra in extraRoots)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    TryAddRoot(extra);
                }
            }
        }

        void TryAddRoot(string raw)
        {
            try
            {
                var full = _pathResolution.GetFullPath(raw);
                var normalised = EnsureTrailingSeparator(full);
                if (!_allowedRoots.Any(r => string.Equals(r, normalised, StringComparison.OrdinalIgnoreCase)))
                {
                    _allowedRoots.Add(normalised);
                }
            }
            catch
            {
                // Ignore malformed configured roots; they will simply not be allowed.
            }
        }
    }

    /// <summary>All roots the policy currently considers allowed.</summary>
    public IReadOnlyList<string> AllowedRoots => _allowedRoots;

    public FilesystemPolicyResult Validate(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return FilesystemPolicyResult.Deny(rawPath ?? string.Empty, "Path must not be blank.");
        }

        if (rawPath.Length > 32767)
        {
            return FilesystemPolicyResult.Deny(rawPath, "Path is too long.");
        }

        if (rawPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return FilesystemPolicyResult.Deny(rawPath, "Path contains invalid characters.");
        }

        // Detect raw traversal markers before normalization.
        if (ContainsTraversalSegment(rawPath))
        {
            return FilesystemPolicyResult.Deny(rawPath, "Path contains traversal ('..') and is not allowed.");
        }

        string normalized;
        try
        {
            normalized = _pathResolution.GetFullPath(rawPath);
        }
        catch (ArgumentException ex)
        {
            return FilesystemPolicyResult.Deny(rawPath, $"Invalid path: {ex.Message}");
        }
        catch (PathTooLongException ex)
        {
            return FilesystemPolicyResult.Deny(rawPath, $"Path too long: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return FilesystemPolicyResult.Deny(rawPath, $"Path not supported: {ex.Message}");
        }

        // If the path itself is a reparse point, resolve it and validate the target.
        if (ExistsAsReparsePoint(normalized))
        {
            var resolved = _pathResolution.ResolveFinalPath(normalized);
            if (resolved is null)
            {
                return FilesystemPolicyResult.Deny(normalized, "Path is a reparse point whose target cannot be resolved and is not allowed.");
            }

            // Reparse must not escape roots.
            try
            {
                resolved = _pathResolution.GetFullPath(resolved);
            }
            catch (Exception ex)
            {
                return FilesystemPolicyResult.Deny(normalized, $"Symlink target is invalid: {ex.Message}");
            }

            if (!IsUnderAllowedRoot(resolved))
            {
                return FilesystemPolicyResult.Deny(normalized, $"Path is a reparse point whose target '{resolved}' is outside the allowed roots and is not allowed.");
            }

            // Also reject if the target is a protected location.
            var protectedReason = GetProtectedReason(resolved);
            if (protectedReason is not null)
            {
                return FilesystemPolicyResult.Deny(normalized, protectedReason);
            }

            // The normalized path to use is the resolved target for further checks.
            // Keep reporting the original normalized path in the error, but return
            // the resolved one as the effective path.
            normalized = resolved;
        }
        else if (IsInsideReparseChain(normalized))
        {
            // Any ancestor of the path is a reparse point that escapes.
            var ancestorResolve = TryResolveNearestReparseAncestor(normalized);
            if (ancestorResolve is not null)
            {
                return FilesystemPolicyResult.Deny(
                    normalized,
                    $"Path traverses a reparse point whose target '{ancestorResolve}' is outside the allowed roots and is not allowed.");
            }

            return FilesystemPolicyResult.Deny(normalized, "Path traverses a reparse point that cannot be safely resolved and is not allowed.");
        }

        // Protected locations (Windows, Program Files, HAMMOR secrets).
        var reason = GetProtectedReason(normalized);
        if (reason is not null)
        {
            return FilesystemPolicyResult.Deny(normalized, reason);
        }

        // Must be under an approved root.
        if (!IsUnderAllowedRoot(normalized))
        {
            var rootsText = string.Join(", ", _allowedRoots.Select(r => $"'{r.TrimEnd(Path.DirectorySeparatorChar)}'"));
            return FilesystemPolicyResult.Deny(
                normalized,
                $"Path '{normalized}' is outside the allowed roots ({rootsText}) and is not allowed.");
        }

        return FilesystemPolicyResult.Allow(normalized);
    }

    // ---- helpers ------------------------------------------------------

    private bool ExistsAsReparsePoint(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return false;
            }

            return _pathResolution.IsReparsePoint(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private bool IsInsideReparseChain(string fullPath)
    {
        try
        {
            var current = Path.GetDirectoryName(fullPath);
            while (!string.IsNullOrEmpty(current) && current.Length >= 3)
            {
                if (Directory.Exists(current) && _pathResolution.IsReparsePoint(current))
                {
                    var resolved = _pathResolution.ResolveFinalPath(current);
                    if (resolved is null)
                    {
                        return true;
                    }

                    try
                    {
                        resolved = _pathResolution.GetFullPath(resolved);
                    }
                    catch
                    {
                        return true;
                    }

                    if (!IsUnderAllowedRoot(resolved))
                    {
                        return true;
                    }

                    // If the ancestor reparse itself points to a protected location, treat as inside.
                    if (GetProtectedReason(resolved) is not null)
                    {
                        return true;
                    }
                }

                current = Path.GetDirectoryName(current);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private string? TryResolveNearestReparseAncestor(string fullPath)
    {
        try
        {
            var current = Path.GetDirectoryName(fullPath);
            while (!string.IsNullOrEmpty(current) && current.Length >= 3)
            {
                if (Directory.Exists(current) && _pathResolution.IsReparsePoint(current))
                {
                    var resolved = _pathResolution.ResolveFinalPath(current);
                    return resolved;
                }

                current = Path.GetDirectoryName(current);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private bool IsUnderAllowedRoot(string normalized)
    {
        foreach (var root in _allowedRoots)
        {
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Exact match without trailing separator.
            var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalized, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string? GetProtectedReason(string normalized)
    {
        // HAMMOR secrets directory is always protected, even though it is under DataRoot.
        // Reads/writes to it must go through ISecretStore/DPAPI, not filesystem tools.
        if (normalized.StartsWith(_secretsDirectory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized.TrimEnd(Path.DirectorySeparatorChar), _secretsDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return $"Path '{normalized}' is inside HAMMOR's secret storage and is not allowed.";
        }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDir))
        {
            var win = EnsureTrailingSeparator(Path.GetFullPath(windowsDir));
            if (normalized.StartsWith(win, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized.TrimEnd(Path.DirectorySeparatorChar), win.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return $"Path '{normalized}' is inside the protected Windows directory and is not allowed.";
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var pf = EnsureTrailingSeparator(Path.GetFullPath(programFiles));
            if (normalized.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
            {
                return $"Path '{normalized}' is inside Program Files and is not allowed.";
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            var pf86 = EnsureTrailingSeparator(Path.GetFullPath(programFilesX86));
            if (normalized.StartsWith(pf86, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized.TrimEnd(Path.DirectorySeparatorChar), pf86.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return $"Path '{normalized}' is inside Program Files (x86) and is not allowed.";
            }
        }

        return null;
    }

    private static bool ContainsTraversalSegment(string raw)
    {
        // Split on both separators; any ".." segment is traversal.
        var parts = raw.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.None);
        foreach (var part in parts)
        {
            if (part == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
