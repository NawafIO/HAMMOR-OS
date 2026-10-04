namespace HAMMOR.Core.Tools;

/// <summary>
/// Default <see cref="IToolRegistry"/>. Name lookup is case-insensitive so a
/// model emitting <c>Memory.Search</c> still resolves.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();

    public IReadOnlyList<ITool> All
    {
        get
        {
            lock (_gate)
            {
                return _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            }
        }
    }

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            throw new ArgumentException("Tool name must not be blank.", nameof(tool));
        }

        lock (_gate)
        {
            if (_tools.ContainsKey(tool.Name))
            {
                throw new InvalidOperationException(
                    $"A tool named '{tool.Name}' is already registered.");
            }

            _tools.Add(tool.Name, tool);
        }
    }

    public ITool? Find(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        lock (_gate)
        {
            return _tools.GetValueOrDefault(toolName);
        }
    }
}
