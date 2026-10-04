using System.Text;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Tools;

namespace HAMMOR.Infrastructure.Tools;

/// <summary>
/// Keyword search across saved memory. Permission level Read: it changes
/// nothing.
/// </summary>
public sealed class SearchMemoryTool(IMemoryStore memoryStore) : ITool
{
    private const int MaxResults = 25;

    private readonly IMemoryStore _memoryStore =
        memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));

    public string Name => "memory.search";

    public string Description =>
        "Search saved memory by keyword. Call this before answering questions that may "
        + "depend on something the user told you in an earlier session.";

    public ToolPermission Permission => ToolPermission.Read;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Keywords to search for." },
            "limit": {
              "type": "integer",
              "description": "Maximum results to return (1-25). Defaults to 10.",
              "minimum": 1,
              "maximum": 25
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """);

    public ToolValidationResult Validate(ToolInvocation invocation)
    {
        if (invocation.GetString("query") is null)
        {
            return ToolValidationResult.Invalid("'query' is required and must not be blank.");
        }

        var limit = invocation.GetInt32("limit", 10);
        return limit is < 1 or > MaxResults
            ? ToolValidationResult.Invalid($"'limit' must be between 1 and {MaxResults}.")
            : ToolValidationResult.Valid;
    }

    public async Task<ToolResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(invocation);
        if (!validation.IsValid)
        {
            return ToolResult.Failure(validation.Error!);
        }

        var query = invocation.GetString("query")!;
        var limit = Math.Clamp(invocation.GetInt32("limit", 10), 1, MaxResults);

        var results = await _memoryStore.SearchAsync(query, limit, cancellationToken)
            .ConfigureAwait(false);

        if (results.Count == 0)
        {
            return ToolResult.Success($"No memory entries matched '{query}'.");
        }

        var output = new StringBuilder()
            .AppendLine($"{results.Count} result(s) for '{query}':")
            .AppendLine();

        foreach (var entry in results)
        {
            output
                .AppendLine($"- {entry.Title} ({entry.Kind}, {entry.ModifiedUtc:yyyy-MM-dd})")
                .AppendLine($"  {Summarise(entry.Content)}");
        }

        return ToolResult.Success(output.ToString().TrimEnd());
    }

    private static string Summarise(string content)
    {
        var collapsed = content
            .ReplaceLineEndings(" ")
            .Trim();

        return collapsed.Length <= 200 ? collapsed : collapsed[..200] + "…";
    }
}
