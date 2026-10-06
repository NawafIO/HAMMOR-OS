namespace HAMMOR.Core.Tools;

/// <summary>
/// A tool whose arguments name filesystem paths it will touch (ADR-004 §2.2).
/// Unattended runs check every declared argument against the task grant's
/// roots before the call reaches the permission evaluator. Declared by the
/// tool so no argument names are hard-coded in the gate.
/// </summary>
public interface IPathScopedTool
{
    /// <summary>Argument names that carry filesystem paths the tool will touch.</summary>
    IReadOnlyList<string> PathArguments { get; }
}

/// <summary>
/// A path-scoped tool that runs Git against its path argument. Unattended
/// runs additionally require that path to contain a real <c>.git</c>
/// directory, because Git's repository discovery walks parent directories
/// and follows <c>.git</c> files (ADR-004 §2.5).
/// </summary>
public interface IGitRepositoryScopedTool : IPathScopedTool
{
}
