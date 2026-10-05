using System.Text.Json;
using HAMMOR.Core.Ai;

namespace HAMMOR.Core.Tools;

/// <summary>
/// Validates a model-produced tool call against the registry and JSON schema
/// without executing anything. The model may produce any string; this gate
/// decides whether it is a well-formed request to run a real tool.
/// </summary>
public static class ToolCallValidator
{
    /// <summary>
    /// Validates that <paramref name="call"/> names a registered tool, that
    /// its JSON parses, and that it satisfies coarse schema rules (required
    /// fields present, unknown properties rejected when additionalProperties
    /// is false, types match). Delegates fine-grained validation to
    /// <see cref="ITool.Validate"/> so there is a single authority on shape.
    /// </summary>
    /// <returns>
    /// On success, <c>IsValid=true</c> and <c>Invocation</c> populated; on
    /// failure, <c>IsValid=false</c> with a safe structured error.
    /// </returns>
    public static (bool IsValid, string? Error, ToolInvocation? Invocation) ValidateAndBuild(
        ModelToolCall call,
        IToolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(registry);

        if (string.IsNullOrWhiteSpace(call.ToolName))
            return (false, "Unknown tool ''.", null);

        var tool = registry.Find(call.ToolName);
        if (tool is null)
            return (false, $"Unknown tool '{call.ToolName}'.", null);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
        }
        catch (JsonException ex)
        {
            return (false, $"Malformed JSON for '{tool.Name}': {ex.Message}", null);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (false, $"Arguments for '{tool.Name}' must be a JSON object.", null);

            // Lightweight schema check against InputSchema.Json before delegating to ITool.Validate.
            var schemaError = CheckAgainstSchema(doc.RootElement, tool.InputSchema.Json, tool.Name);
            if (schemaError is not null)
                return (false, schemaError, null);

            // Build string-valued argument map as expected by ToolInvocation.
            var args = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                args[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.GetRawText();
                // Numeric/boolean/null raw text is intentional: callers like
                // GetInt32 do their own TryParse, matching the existing tools.
                // String values are unquoted via GetString().
            }

            var invocation = new ToolInvocation
            {
                InvocationId = string.IsNullOrWhiteSpace(call.Id) ? Guid.NewGuid().ToString("n") : call.Id,
                ToolName = tool.Name,
                Arguments = args,
            };

            var result = tool.Validate(invocation);
            if (!result.IsValid)
                return (false, result.Error ?? $"Arguments for '{tool.Name}' are not valid.", null);

            return (true, null, invocation);
        }
    }

    /// <summary>
    /// Coarse JSON Schema 2020-12 subset check: required, additionalProperties,
    /// and primitive type matching (string/integer/number/boolean/array/object).
    /// Does not interpret $ref or complex combinators; those are left to
    /// <see cref="ITool.Validate"/> along with range checks.
    /// </summary>
    private static string? CheckAgainstSchema(JsonElement args, string schemaJson, string toolName)
    {
        JsonDocument schemaDoc;
        try { schemaDoc = JsonDocument.Parse(schemaJson); }
        catch { return null; } // malformed schema => skip second gate, defer to ITool.Validate
        using (schemaDoc)
        {
            var root = schemaDoc.RootElement;
            if (!root.TryGetProperty("properties", out var propsEl) || propsEl.ValueKind != JsonValueKind.Object)
                return null;

            var required = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array)
                foreach (var r in reqEl.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String && r.GetString() is { } s) required.Add(s);

            bool additionalAllowed = true;
            if (root.TryGetProperty("additionalProperties", out var addEl) && addEl.ValueKind == JsonValueKind.False)
                additionalAllowed = false;

            // required
            foreach (var req in required)
                if (!args.TryGetProperty(req, out _))
                    return $"Missing required property '{req}' for '{toolName}'.";

            // unknown properties
            if (!additionalAllowed)
                foreach (var p in args.EnumerateObject())
                    if (!propsEl.TryGetProperty(p.Name, out _))
                        return $"Unknown property '{p.Name}' for '{toolName}'.";

            // type checks
            foreach (var p in args.EnumerateObject())
            {
                if (!propsEl.TryGetProperty(p.Name, out var propSchema)) continue;
                if (!propSchema.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                    continue;
                var expected = typeEl.GetString();
                if (expected is null) continue;
                var actual = p.Value.ValueKind;
                bool ok = expected switch
                {
                    "string" => actual == JsonValueKind.String,
                    "integer" => actual == JsonValueKind.Number && p.Value.TryGetInt64(out _),
                    "number" => actual == JsonValueKind.Number,
                    "boolean" => actual == JsonValueKind.True || actual == JsonValueKind.False,
                    "array" => actual == JsonValueKind.Array,
                    "object" => actual == JsonValueKind.Object,
                    _ => true,
                };
                if (!ok)
                    return $"Property '{p.Name}' must be {expected} for '{toolName}'.";
            }

            return null;
        }
    }
}
