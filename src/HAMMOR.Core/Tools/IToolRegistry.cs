namespace HAMMOR.Core.Tools;

/// <summary>
/// The set of tools HAMMOR can invoke. New tools register here without any
/// change to the agent loop or the permission engine.
/// </summary>
public interface IToolRegistry
{
    /// <summary>Every registered tool, ordered by name.</summary>
    IReadOnlyList<ITool> All { get; }

    /// <summary>Adds a tool.</summary>
    /// <exception cref="InvalidOperationException">
    /// A tool with the same name is already registered. Silently replacing it
    /// would let one component shadow another's capability.
    /// </exception>
    void Register(ITool tool);

    /// <summary>Finds a tool by name, or null when not registered.</summary>
    ITool? Find(string toolName);
}
