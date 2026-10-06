using System.Text;
using System.Text.RegularExpressions;

namespace HAMMOR.Core.Tools.Git;

/// <summary>
/// Refuses repository layouts and config that would let git read outside the
/// directory it was given (ADR-005). The path must already have passed
/// <see cref="HAMMOR.Core.Tools.Filesystem.IFilesystemPolicy"/>; this guard
/// adds to that check and never replaces it.
/// </summary>
/// <remarks>
/// The config checks are deliberately coarse supersets: a false refusal is
/// acceptable, a missed redirection is not. <see cref="SafeGitRunner"/> also
/// pins the work tree and common directory through the environment, so a
/// <c>core.worktree</c> or <c>commondir</c> that appears after this check
/// still has no effect.
/// </remarks>
internal static class GitRepositoryGuard
{
    internal const int MaxConfigBytes = 1024 * 1024;

    // Any "[include" or "[includeIf" header, also after another header on the
    // same line ("[core][include] path = x" is a valid include).
    private static readonly Regex IncludeSection =
        new(@"\[\s*include", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // core.worktree in either form: "[core]\n worktree = x" or "[core] worktree = x".
    private static readonly Regex WorktreeKey =
        new(@"\bworktree\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns why <paramref name="repositoryRoot"/> must not be handed to git,
    /// or null when it may be.
    /// </summary>
    public static string? FindProblem(string repositoryRoot)
    {
        var problem = FindProblemCore(repositoryRoot);
        return problem is null ? null : $"Repository '{repositoryRoot}' is not allowed: {problem}";
    }

    private static string? FindProblemCore(string repositoryRoot)
    {
        var gitDir = Path.Combine(repositoryRoot, ".git");

        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(gitDir);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return "it has no .git directory. Pass the repository root; parent-directory discovery is not allowed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"its .git entry cannot be inspected ({ex.Message}).";
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return "'.git' is a reparse point (symbolic link or junction).";
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            return "'.git' is a file (gitdir indirection, as used by linked worktrees and submodule checkouts).";
        }

        try
        {
            foreach (var entry in new DirectoryInfo(gitDir).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return $"'.git/{entry.Name}' is a reparse point (symbolic link or junction).";
                }
            }

            if (Path.Exists(Path.Combine(gitDir, "commondir")))
            {
                return "'.git/commondir' redirects refs, objects and config to another directory.";
            }

            if (Path.Exists(Path.Combine(gitDir, "objects", "info", "alternates")))
            {
                return "'.git/objects/info/alternates' makes git read objects from other directories.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"its .git directory cannot be inspected ({ex.Message}).";
        }

        return FindConfigProblem(gitDir, "config") ?? FindConfigProblem(gitDir, "config.worktree");
    }

    private static string? FindConfigProblem(string gitDir, string fileName)
    {
        var path = Path.Combine(gitDir, fileName);
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if (new FileInfo(path).Length > MaxConfigBytes)
            {
                return $"'.git/{fileName}' is too large to inspect.";
            }

            // Git parses config as bytes; Latin-1 maps each byte to one char,
            // so no byte sequence can hide an ASCII header from the patterns.
            text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"'.git/{fileName}' cannot be inspected ({ex.Message}).";
        }

        if (IncludeSection.IsMatch(text))
        {
            return $"'.git/{fileName}' contains an [include] or [includeIf] section, which loads config from other files.";
        }

        if (WorktreeKey.IsMatch(text))
        {
            return $"'.git/{fileName}' sets a worktree key (core.worktree), which points git at another directory.";
        }

        return null;
    }
}
