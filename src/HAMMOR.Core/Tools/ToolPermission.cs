namespace HAMMOR.Core.Tools;

/// <summary>
/// How much authority a tool needs. Ordered least to most dangerous so the
/// permission engine can compare with <c>&lt;=</c> against a configured
/// auto-approve ceiling.
/// </summary>
public enum ToolPermission
{
    /// <summary>Observes state without changing it.</summary>
    Read = 0,

    /// <summary>Creates or modifies data, recoverably.</summary>
    Write = 1,

    /// <summary>Runs a process or causes an external side effect.</summary>
    Execute = 2,

    /// <summary>
    /// Irreversible or wide-blast-radius: deletion, overwrite without backup,
    /// anything that cannot be undone. Always requires explicit confirmation.
    /// </summary>
    Destructive = 3,
}
