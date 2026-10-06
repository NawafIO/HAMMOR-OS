using HAMMOR.Core.Tools;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Core.Tools.Git;

namespace HAMMOR.Core.Tasks;

/// <param name="Errors">Why roots were rejected; empty when valid.</param>
/// <param name="CanonicalRoots">Policy-canonical roots with a trailing separator.</param>
public sealed record GrantRootValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> CanonicalRoots)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Path-level narrowing for unattended runs (ADR-004 §2). Every decision is
/// made on the path <see cref="IFilesystemPolicy"/> resolves, never on the raw
/// argument, and only ever narrows what the policy already allows.
/// </summary>
public sealed class TaskPathScope
{
    private readonly IFilesystemPolicy _policy;
    private readonly IPathResolution _pathResolution;

    public TaskPathScope(IFilesystemPolicy policy, IPathResolution pathResolution)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _pathResolution = pathResolution ?? throw new ArgumentNullException(nameof(pathResolution));
    }

    /// <summary>
    /// Validates grant roots: each must pass the policy, be an existing
    /// directory, not be a reparse point and not lie inside a reparse chain
    /// (the same rule the policy applies to configured roots).
    /// </summary>
    public GrantRootValidation ValidateRoots(IReadOnlyList<string>? roots)
    {
        var errors = new List<string>();
        var canonical = new List<string>();

        if (roots is null)
        {
            errors.Add("AllowedRoots must not be null.");
            return new GrantRootValidation(errors, canonical);
        }

        if (roots.Count > TaskGrantValidator.MaxAllowedRoots)
        {
            errors.Add($"A grant may list at most {TaskGrantValidator.MaxAllowedRoots} roots.");
        }

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                errors.Add("Root paths must not be blank.");
                continue;
            }

            var validation = _policy.Validate(root);
            if (!validation.IsAllowed)
            {
                errors.Add($"Root '{root}' is not allowed: {validation.Error}");
                continue;
            }

            if (!DirectoryExists(validation.NormalizedPath))
            {
                errors.Add($"Root '{root}' must be an existing directory.");
                continue;
            }

            var linkProblem = FindLinkProblem(root);
            if (linkProblem is not null)
            {
                errors.Add(linkProblem);
                continue;
            }

            var normalised = EnsureTrailingSeparator(validation.NormalizedPath);
            if (canonical.Any(r => string.Equals(r, normalised, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Root '{root}' is listed more than once.");
                continue;
            }

            canonical.Add(normalised);
        }

        return new GrantRootValidation(errors, canonical);
    }

    /// <summary>
    /// Run-time check for one call. Non-path-scoped tools are unaffected.
    /// Any missing argument, policy denial, or resolved path outside the
    /// granted roots blocks the call.
    /// </summary>
    public ToolGateDecision CheckCall(ITool tool, ToolInvocation invocation, IReadOnlyList<string>? grantRoots)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(invocation);

        if (tool is not IPathScopedTool scoped)
        {
            return ToolGateDecision.Allow;
        }

        if (grantRoots is null || grantRoots.Count == 0)
        {
            return ToolGateDecision.Block(
                $"'{tool.Name}' touches the filesystem but the task's grant names no roots.");
        }

        if (scoped.PathArguments is null || scoped.PathArguments.Count == 0)
        {
            return ToolGateDecision.Block(
                $"'{tool.Name}' is path-scoped but declares no path arguments.");
        }

        foreach (var argument in scoped.PathArguments)
        {
            var raw = invocation.GetString(argument);
            if (raw is null)
            {
                return ToolGateDecision.Block(
                    $"'{tool.Name}' was called without its '{argument}' path.");
            }

            var validation = _policy.Validate(raw);
            if (!validation.IsAllowed)
            {
                return ToolGateDecision.Block(
                    $"'{argument}' for '{tool.Name}' was refused by the filesystem policy: {validation.Error}");
            }

            var resolved = validation.NormalizedPath;
            if (!IsUnderAny(resolved, grantRoots))
            {
                return ToolGateDecision.Block(
                    $"'{argument}' for '{tool.Name}' resolves to '{resolved}', which is outside the task's granted roots.");
            }

            if (tool is IGitRepositoryScopedTool)
            {
                var gitProblem = FindGitDirectoryProblem(resolved, grantRoots);
                if (gitProblem is not null)
                {
                    return ToolGateDecision.Block($"'{tool.Name}': {gitProblem}");
                }
            }
        }

        return ToolGateDecision.Allow;
    }

    /// <summary>Segment-aware, case-insensitive containment on canonical paths.</summary>
    public static bool IsUnderAny(string path, IReadOnlyList<string> roots)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var probe = EnsureTrailingSeparator(path);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (probe.StartsWith(EnsureTrailingSeparator(root), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ADR-004 §2.5 as superseded by ADR-005: the repository rules are
    /// <see cref="GitRepositoryGuard"/>'s, shared with interactive calls, so
    /// unattended runs never apply a weaker or divergent Git check. This
    /// method only adds what is specific to a grant: the injected reparse
    /// view of <c>.git</c> and containment in the granted roots.
    /// </summary>
    private string? FindGitDirectoryProblem(string repositoryPath, IReadOnlyList<string> grantRoots)
    {
        var guardProblem = GitRepositoryGuard.FindProblem(repositoryPath);
        if (guardProblem is not null)
        {
            return guardProblem;
        }

        var gitDirectory = Path.Combine(repositoryPath, ".git");

        try
        {
            if (_pathResolution.IsReparsePoint(gitDirectory))
            {
                return "the repository's '.git' directory is a reparse point, which unattended runs do not follow.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"the repository's '.git' directory could not be inspected: {ex.Message}";
        }

        return IsUnderAny(gitDirectory, grantRoots)
            ? null
            : "the repository's '.git' directory is outside the task's granted roots.";
    }

    private string? FindLinkProblem(string root)
    {
        string full;
        try
        {
            full = _pathResolution.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Root '{root}' is not a valid path: {ex.Message}";
        }

        try
        {
            if (DirectoryExists(full) && _pathResolution.IsReparsePoint(full))
            {
                return $"Root '{root}' is a reparse point (link or junction) and cannot be granted.";
            }

            var current = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            while (!string.IsNullOrEmpty(current))
            {
                if (DirectoryExists(current) && _pathResolution.IsReparsePoint(current))
                {
                    return $"Root '{root}' lies inside a reparse point ('{current}') and cannot be granted.";
                }

                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Root '{root}' could not be inspected: {ex.Message}";
        }

        return null;
    }

    private static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
