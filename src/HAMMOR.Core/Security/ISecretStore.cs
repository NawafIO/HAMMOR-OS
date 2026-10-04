namespace HAMMOR.Core.Security;

/// <summary>
/// Stores API credentials outside the configuration file and outside source
/// control. The Windows implementation encrypts with DPAPI scoped to the
/// current user.
/// </summary>
/// <remarks>
/// Keys are referenced by the well-known names on <see cref="SecretNames"/>.
/// Nothing in HAMMOR should ever log a retrieved value — use
/// <see cref="Diagnostics.SecretRedactor.DescribePresence"/> when the UI needs
/// to show whether a credential exists.
/// </remarks>
public interface ISecretStore
{
    /// <summary>Retrieves a secret, or null when none is stored.</summary>
    Task<string?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Stores or replaces a secret.</summary>
    Task SetAsync(string name, string value, CancellationToken cancellationToken = default);

    /// <summary>Removes a secret. A no-op when it does not exist.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a secret is present, without decrypting it. Used by status
    /// indicators so "configured?" never requires handling the value.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>Well-known secret names.</summary>
public static class SecretNames
{
    public const string AnthropicApiKey = "anthropic.api_key";

    public const string ElevenLabsApiKey = "elevenlabs.api_key";
}
