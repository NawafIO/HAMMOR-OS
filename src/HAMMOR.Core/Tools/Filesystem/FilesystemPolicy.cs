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
        // Do not follow reparse for the roots themselves — a configured root that is a
        // symlink/junction is treated as untrusted and rejected in TryAddRoot. Using
        // GetFullPath alone preserves the link name for the trust check.
        _dataRoot = EnsureTrailingSeparator(_pathResolution.GetFullPath(hammorPaths.DataRoot));
        _secretsDirectory = EnsureTrailingSeparator(_pathResolution.GetFullPath(hammorPaths.SecretsDirectory));

        _allowedRoots = new List<string> { _dataRoot };

        var memoryRoot = configurationStore.Current.Memory.RootPath;
        if (!string.IsNullOrWhiteSpace(memoryRoot))
        {
            TryAddRoot(memoryRoot);
        }

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
                var fullRaw = _pathResolution.GetFullPath(raw);
                // Any configured root that is itself a reparse point (or traverses one) is
                // untrusted — do not add it or its target. This prevents an attacker from
                // configuring a symlink that redirects an allowed root outside the boundary.
                if (ExistsAsReparsePoint(fullRaw))
                {
                    return;
                }

                if (IsInsideReparseChain(fullRaw, out _))
                {
                    return;
                }

                var full = Canonicalize(raw);
                var normalised = EnsureTrailingSeparator(full);
                if (!_allowedRoots.Any(r => string.Equals(r, normalised, StringComparison.OrdinalIgnoreCase)))
                {
                    if (GetProtectedReason(normalised) is not null)
                    {
                        return;
                    }

                    _allowedRoots.Add(normalised);
                }
            }
            catch
            {
            }
        }
    }

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

        if (ContainsTraversalSegment(rawPath))
        {
            return FilesystemPolicyResult.Deny(rawPath, "Path contains traversal ('..') and is not allowed.");
        }

        string normalized;
        try
        {
            normalized = Canonicalize(rawPath);
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

        if (ExistsAsReparsePoint(normalized))
        {
            var resolved = _pathResolution.ResolveFinalPath(normalized);
            if (resolved is null)
            {
                return FilesystemPolicyResult.Deny(normalized, "Path is a reparse point whose target cannot be resolved and is not allowed.");
            }

            try
            {
                resolved = Canonicalize(resolved);
            }
            catch (Exception ex)
            {
                return FilesystemPolicyResult.Deny(normalized, $"Symlink target is invalid: {ex.Message}");
            }

            if (!IsUnderAllowedRoot(resolved))
            {
                return FilesystemPolicyResult.Deny(normalized, $"Path is a reparse point whose target '{resolved}' is outside the allowed roots and is not allowed.");
            }

            var protectedReason = GetProtectedReason(resolved);
            if (protectedReason is not null)
            {
                return FilesystemPolicyResult.Deny(normalized, protectedReason);
            }

            normalized = resolved;
        }
        else if (IsInsideReparseChain(normalized, out var ancestorTarget))
        {
            if (ancestorTarget is not null)
            {
                return FilesystemPolicyResult.Deny(
                    normalized,
                    $"Path traverses a reparse point whose target '{ancestorTarget}' is outside the allowed roots and is not allowed.");
            }

            return FilesystemPolicyResult.Deny(normalized, "Path traverses a reparse point that cannot be safely resolved and is not allowed.");
        }

        var reason = GetProtectedReason(normalized);
        if (reason is not null)
        {
            return FilesystemPolicyResult.Deny(normalized, reason);
        }

        if (!IsUnderAllowedRoot(normalized))
        {
            var rootsText = string.Join(", ", _allowedRoots.Select(r => $"'{r.TrimEnd(Path.DirectorySeparatorChar)}'"));
            return FilesystemPolicyResult.Deny(
                normalized,
                $"Path '{normalized}' is outside the allowed roots ({rootsText}) and is not allowed.");
        }

        return FilesystemPolicyResult.Allow(normalized);
    }

    private string Canonicalize(string path)
    {
        var full = _pathResolution.GetFullPath(path);
        try
        {
            var remainder = string.Empty;
            var probe = full;
            while (!string.IsNullOrEmpty(probe))
            {
                if (File.Exists(probe) || Directory.Exists(probe))
                {
                    var resolved = _pathResolution.ResolveFinalPath(probe);
                    if (resolved is not null)
                    {
                        try { resolved = _pathResolution.GetFullPath(resolved); } catch { resolved = null; }
                    }

                    if (resolved is not null && !string.IsNullOrEmpty(remainder))
                    {
                        resolved = Path.Combine(resolved, remainder);
                        try { resolved = _pathResolution.GetFullPath(resolved); } catch { }
                    }

                    if (resolved is not null)
                    {
                        return resolved;
                    }

                    break;
                }

                var parent = Path.GetDirectoryName(probe);
                if (string.IsNullOrEmpty(parent) || parent.Length < 3)
                {
                    break;
                }

                var leaf = Path.GetFileName(probe);
                if (string.IsNullOrEmpty(leaf))
                {
                    break;
                }

                remainder = string.IsNullOrEmpty(remainder) ? leaf : Path.Combine(leaf, remainder);
                probe = parent;
            }
        }
        catch
        {
        }

        return full;
    }

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

    private bool IsInsideReparseChain(string fullPath, out string? escapingTarget)
    {
        escapingTarget = null;
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
                        escapingTarget = null;
                        return true;
                    }

                    try
                    {
                        resolved = Canonicalize(resolved);
                    }
                    catch
                    {
                        escapingTarget = resolved;
                        return true;
                    }

                    if (!IsUnderAllowedRoot(resolved))
                    {
                        escapingTarget = resolved;
                        return true;
                    }

                    if (GetProtectedReason(resolved) is not null)
                    {
                        escapingTarget = resolved;
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

    private bool IsInsideReparseChain(string fullPath) => IsInsideReparseChain(fullPath, out _);

    private bool IsUnderAllowedRoot(string normalized)
    {
        var probe = normalized;
        foreach (var root in _allowedRoots)
        {
            if (probe.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(probe.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string? GetProtectedReason(string normalized)
    {
        var probe = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var withSep = EnsureTrailingSeparator(probe);

        if (withSep.StartsWith(_secretsDirectory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(probe, _secretsDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return $"Path '{normalized}' is inside HAMMOR's secret storage and is not allowed.";
        }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDir))
        {
            string win;
            try { win = EnsureTrailingSeparator(Canonicalize(windowsDir)); } catch { win = EnsureTrailingSeparator(Path.GetFullPath(windowsDir)); }
            if (withSep.StartsWith(win, StringComparison.OrdinalIgnoreCase)
                || string.Equals(probe, win.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return $"Path '{normalized}' is inside the protected Windows directory and is not allowed.";
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            string pf;
            try { pf = EnsureTrailingSeparator(Canonicalize(programFiles)); } catch { pf = EnsureTrailingSeparator(Path.GetFullPath(programFiles)); }
            if (withSep.StartsWith(pf, StringComparison.OrdinalIgnoreCase)
                || string.Equals(probe, pf.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return $"Path '{normalized}' is inside Program Files and is not allowed.";
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            string pf86;
            try { pf86 = EnsureTrailingSeparator(Canonicalize(programFilesX86)); } catch { pf86 = EnsureTrailingSeparator(Path.GetFullPath(programFilesX86)); }
            if (withSep.StartsWith(pf86, StringComparison.OrdinalIgnoreCase)
                || string.Equals(probe, pf86.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return $"Path '{normalized}' is inside Program Files (x86) and is not allowed.";
            }
        }

        return null;
    }

    private static bool ContainsTraversalSegment(string raw)
    {
        var parts = raw.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.None);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed == "..")
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
