namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Centralised filesystem security policy. All filesystem tools must consult
/// this before touching the OS. Denials become typed <see cref="ToolResult"/>
/// failures, never exceptions or silent behaviour.
/// </summary>
public interface IFilesystemPolicy
{
    /// <summary>
    /// Validates that <paramref name="rawPath"/> is allowed under the current
    /// policy. Implementations must normalise the path and check roots,
    /// traversal, protected locations, secrets, and reparse escapes.
    /// </summary>
    FilesystemPolicyResult Validate(string rawPath);
}

/// <summary>Outcome of a policy check.</summary>
/// <param name="IsAllowed">Whether the caller may proceed.</param>
/// <param name="NormalizedPath">Full normalised path when allowed or when the error can name it.</param>
/// <param name="Error">Human-readable reason when denied.</param>
public sealed record FilesystemPolicyResult(bool IsAllowed, string NormalizedPath, string? Error)
{
    public static FilesystemPolicyResult Allow(string normalizedPath) => new(true, normalizedPath, null);

    public static FilesystemPolicyResult Deny(string normalizedPath, string error) => new(false, normalizedPath, error);
}
