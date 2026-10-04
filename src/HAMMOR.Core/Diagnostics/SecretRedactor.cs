using System.Text.RegularExpressions;

namespace HAMMOR.Core.Diagnostics;

/// <summary>
/// Masks credential-shaped substrings before text reaches a log sink, an audit
/// row, or the UI.
/// </summary>
/// <remarks>
/// This is defence in depth, not the primary control: secrets are held in
/// Windows DPAPI and are never meant to flow into log messages at all. The
/// redactor exists because provider SDKs and HTTP error bodies occasionally
/// echo a key back inside an exception message.
/// </remarks>
public static partial class SecretRedactor
{
    public const string Mask = "[redacted]";

    // Anthropic keys (sk-ant-...), generic sk- keys, and ElevenLabs keys.
    [GeneratedRegex(@"sk-ant-[A-Za-z0-9\-_]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9\-_]{16,}", RegexOptions.IgnoreCase)]
    private static partial Regex GenericSecretKey();

    [GeneratedRegex(@"\bxi-api-key\s*[:=]\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex ElevenLabsHeader();

    [GeneratedRegex(@"\b(api[_-]?key|authorization|bearer|x-api-key|password|token)\b\s*[:=]\s*[""']?[^\s""',}]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex LabelledSecret();

    /// <summary>
    /// Returns <paramref name="text"/> with anything that looks like a
    /// credential replaced by <see cref="Mask"/>.
    /// </summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = AnthropicKey().Replace(text, Mask);
        result = GenericSecretKey().Replace(result, Mask);
        result = ElevenLabsHeader().Replace(result, $"xi-api-key: {Mask}");
        result = LabelledSecret().Replace(result, match =>
        {
            var label = match.Groups[1].Value;
            return $"{label}: {Mask}";
        });

        return result;
    }

    /// <summary>
    /// Renders a secret for display: never the value, only whether one is set
    /// and a short non-reversible hint of its length.
    /// </summary>
    public static string DescribePresence(string? secret) =>
        string.IsNullOrWhiteSpace(secret) ? "not set" : $"set ({secret.Length} chars)";
}
