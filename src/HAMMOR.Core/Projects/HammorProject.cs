namespace HAMMOR.Core.Projects;

/// <summary>
/// A first-class workspace that owns its own context, memory, tasks and
/// activity.
/// </summary>
public sealed record HammorProject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Absolute path on disk, when the project maps to a folder. Null for
    /// projects that exist only as a context grouping.
    /// </summary>
    public string? RootPath { get; init; }

    public ProjectKind Kind { get; init; } = ProjectKind.General;

    /// <summary>
    /// Standing context handed to the model for every turn scoped to this
    /// project.
    /// </summary>
    public string? Context { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ModifiedUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastOpenedUtc { get; init; }

    public bool IsArchived { get; init; }
}

public enum ProjectKind
{
    General = 0,

    /// <summary>
    /// A codebase. Software projects follow INSPECT → UNDERSTAND → PLAN →
    /// MODIFY → TEST → VERIFY → REPORT.
    /// </summary>
    Software = 1,

    Research = 2,
}

/// <summary>
/// Git facts about a project, read on demand rather than cached on the project
/// record so the UI never shows stale branch information.
/// </summary>
/// <param name="IsRepository">Whether <see cref="HammorProject.RootPath"/> is a git work tree.</param>
/// <param name="Branch">Current branch, or null when detached/unavailable.</param>
/// <param name="HasUncommittedChanges">Whether the work tree is dirty.</param>
/// <param name="Remote">Origin URL, when configured.</param>
public sealed record ProjectGitInfo(
    bool IsRepository,
    string? Branch,
    bool HasUncommittedChanges,
    string? Remote)
{
    public static ProjectGitInfo NotARepository { get; } = new(false, null, false, null);
}
