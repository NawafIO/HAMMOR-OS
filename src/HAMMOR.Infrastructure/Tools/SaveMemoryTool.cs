using HAMMOR.Core.Memory;
using HAMMOR.Core.Tools;

namespace HAMMOR.Infrastructure.Tools;

/// <summary>
/// Writes a note into HAMMOR's persistent memory. Permission level Write: it
/// creates data but destroys nothing.
/// </summary>
public sealed class SaveMemoryTool(IMemoryStore memoryStore) : ITool
{
    private const int MaxTitleLength = 200;
    private const int MaxContentLength = 100_000;

    private readonly IMemoryStore _memoryStore =
        memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));

    public string Name => "memory.save";

    public string Description =>
        "Save a note to long-term memory. Call this when the user shares a durable "
        + "preference, decision, or fact that should survive beyond this conversation.";

    public ToolPermission Permission => ToolPermission.Write;

    public ToolInputSchema InputSchema { get; } = new("""
        {
          "type": "object",
          "properties": {
            "title":   { "type": "string", "description": "Short label for the note." },
            "content": { "type": "string", "description": "The note body." },
            "kind":    {
              "type": "string",
              "enum": ["Note", "Fact", "Preference", "ProjectContext"],
              "description": "Category of memory. Defaults to Note."
            }
          },
          "required": ["title", "content"],
          "additionalProperties": false
        }
        """);

    public ToolValidationResult Validate(ToolInvocation invocation)
    {
        var title = invocation.GetString("title");
        if (title is null)
        {
            return ToolValidationResult.Invalid("'title' is required.");
        }

        if (title.Length > MaxTitleLength)
        {
            return ToolValidationResult.Invalid(
                $"'title' must be {MaxTitleLength} characters or fewer.");
        }

        var content = invocation.GetString("content");
        if (content is null)
        {
            return ToolValidationResult.Invalid("'content' is required.");
        }

        if (content.Length > MaxContentLength)
        {
            return ToolValidationResult.Invalid(
                $"'content' must be {MaxContentLength} characters or fewer.");
        }

        var kind = invocation.GetString("kind");
        if (kind is not null && !Enum.TryParse<MemoryKind>(kind, ignoreCase: true, out _))
        {
            return ToolValidationResult.Invalid(
                $"'kind' must be one of: {string.Join(", ", Enum.GetNames<MemoryKind>())}.");
        }

        return ToolValidationResult.Valid;
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

        var kind = MemoryKind.Note;
        var requestedKind = invocation.GetString("kind");
        if (requestedKind is not null)
        {
            Enum.TryParse(requestedKind, ignoreCase: true, out kind);
        }

        var saved = await _memoryStore.SaveAsync(
            new MemoryEntry
            {
                Title = invocation.GetString("title")!,
                Content = invocation.GetString("content")!,
                Kind = kind,
                ProjectId = invocation.ProjectId,
            },
            cancellationToken).ConfigureAwait(false);

        return ToolResult.Success(
            $"Saved memory '{saved.Title}' (id {saved.Id})."
            + (saved.MarkdownPath is null
                ? " The markdown mirror could not be written; see the log."
                : $" Markdown: {saved.MarkdownPath}"));
    }
}
