using HAMMOR.Core.Tools;

namespace HAMMOR.Core.Tasks;

/// <summary>
/// Authority a user grants to ONE task for unattended execution (ADR-003).
/// A grant is immutable: it is never edited, extended or re-activated. A new
/// authorisation is a new grant that supersedes the old one.
/// </summary>
public sealed record TaskGrant
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    /// <summary>The single task this grant authorises.</summary>
    public required string TaskId { get; init; }

    /// <summary>Exact tool names. Empty means a text-only task. No wildcards.</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>
    /// Directories that path-scoped tools may touch (ADR-004 §2). Required
    /// when any listed tool is <see cref="IPathScopedTool"/>. Stored in the
    /// filesystem policy's canonical form with a trailing separator.
    /// </summary>
    public IReadOnlyList<string> AllowedRoots { get; init; } = [];

    /// <summary>Ceiling. In this version it must be <see cref="ToolPermission.Read"/>.</summary>
    public ToolPermission MaxPermission { get; init; } = ToolPermission.Read;

    public DateTimeOffset GrantedUtc { get; init; }

    /// <summary>Mandatory and finite; there is no "forever" grant.</summary>
    public required DateTimeOffset ExpiresUtc { get; init; }

    /// <summary>Hard cap on tool calls per run.</summary>
    public int MaxToolCalls { get; init; }

    /// <summary>Optional project scope; when set it must equal the task's project.</summary>
    public string? ProjectId { get; init; }

    /// <summary>Id of the grant this one replaces, when it resumed a Blocked task.</summary>
    public string? SupersedesGrantId { get; init; }

    /// <summary>Set once this grant has been replaced; it can never authorise a run again.</summary>
    public DateTimeOffset? SupersededUtc { get; init; }
}

/// <summary>
/// Creation rules for <see cref="TaskGrant"/>. Applied when a grant is stored
/// and again at run time, so a grant that was valid yesterday is not trusted today.
/// </summary>
public static class TaskGrantValidator
{
    /// <summary>Longest permitted <c>ExpiresUtc - GrantedUtc</c>.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);

    public const int MaxToolCallsLimit = 50;

    public const int MaxAllowedTools = 32;

    public const int MaxAllowedRoots = 16;

    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Registry-independent checks. Returns error messages; empty means valid.</summary>
    public static IReadOnlyList<string> ValidateStructure(TaskGrant? grant, DateTimeOffset nowUtc)
    {
        var errors = new List<string>();

        if (grant is null)
        {
            errors.Add("A grant is required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(grant.Id))
        {
            errors.Add("Grant id must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(grant.TaskId))
        {
            errors.Add("Grant must name its task.");
        }

        if (grant.MaxPermission != ToolPermission.Read)
        {
            errors.Add(
                $"Unattended grants are limited to Read; '{grant.MaxPermission}' is not grantable.");
        }

        if (grant.ExpiresUtc <= nowUtc)
        {
            errors.Add("Grant has expired or its expiry is not in the future.");
        }

        if (grant.ExpiresUtc <= grant.GrantedUtc)
        {
            errors.Add("Grant expiry must be after the time it was granted.");
        }
        else if (grant.ExpiresUtc - grant.GrantedUtc > MaxLifetime)
        {
            errors.Add($"Grant lifetime must not exceed {MaxLifetime.TotalDays:0} days.");
        }

        if (grant.GrantedUtc > nowUtc + ClockSkew)
        {
            errors.Add("Grant is dated in the future.");
        }

        if (grant.MaxToolCalls < 1 || grant.MaxToolCalls > MaxToolCallsLimit)
        {
            errors.Add($"MaxToolCalls must be between 1 and {MaxToolCallsLimit}.");
        }

        var tools = grant.AllowedTools;
        if (tools is null)
        {
            errors.Add("AllowedTools must not be null.");
        }
        else
        {
            if (tools.Count > MaxAllowedTools)
            {
                errors.Add($"A grant may list at most {MaxAllowedTools} tools.");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in tools)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add("Tool names must not be blank.");
                }
                else if (name.AsSpan().IndexOfAny('*', '?') >= 0)
                {
                    errors.Add($"Wildcards are not allowed in tool names ('{name}').");
                }
                else if (!seen.Add(name))
                {
                    errors.Add($"Tool '{name}' is listed more than once.");
                }
            }
        }

        var roots = grant.AllowedRoots;
        if (roots is null)
        {
            errors.Add("AllowedRoots must not be null.");
        }
        else
        {
            if (roots.Count > MaxAllowedRoots)
            {
                errors.Add($"A grant may list at most {MaxAllowedRoots} roots.");
            }

            var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    errors.Add("Root paths must not be blank.");
                }
                else if (root.AsSpan().IndexOfAny('*', '?') >= 0)
                {
                    errors.Add($"Wildcards are not allowed in root paths ('{root}').");
                }
                else if (!seenRoots.Add(root.TrimEnd('\\', '/')))
                {
                    errors.Add($"Root '{root}' is listed more than once.");
                }
            }
        }

        return errors;
    }

    /// <summary>Checks every listed tool exists by exact name and needs only Read.</summary>
    public static IReadOnlyList<string> ValidateAgainstRegistry(TaskGrant grant, IToolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(registry);

        var errors = new List<string>();
        var listsPathScopedTool = false;

        foreach (var name in grant.AllowedTools ?? Array.Empty<string>())
        {
            var tool = registry.Find(name);
            if (tool is null)
            {
                errors.Add($"Tool '{name}' is not registered.");
            }
            else if (!string.Equals(tool.Name, name, StringComparison.Ordinal))
            {
                errors.Add($"Use the exact tool name '{tool.Name}' instead of '{name}'.");
            }
            else if (tool.Permission != ToolPermission.Read)
            {
                errors.Add(
                    $"Tool '{name}' needs {tool.Permission}; only Read tools are grantable.");
            }

            if (tool is IPathScopedTool)
            {
                listsPathScopedTool = true;
            }
        }

        // ADR-004 §2.3: no "any allowed path" grant for path-scoped tools.
        if (listsPathScopedTool && (grant.AllowedRoots is null || grant.AllowedRoots.Count == 0))
        {
            errors.Add("A grant that lists filesystem, Git or project tools must name at least one root.");
        }

        return errors;
    }

    /// <summary>Structure plus registry checks.</summary>
    public static IReadOnlyList<string> Validate(
        TaskGrant? grant,
        IToolRegistry registry,
        DateTimeOffset nowUtc)
    {
        var errors = ValidateStructure(grant, nowUtc).ToList();
        if (grant is not null && grant.AllowedTools is not null)
        {
            errors.AddRange(ValidateAgainstRegistry(grant, registry));
        }

        return errors;
    }
}
