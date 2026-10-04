namespace HAMMOR.Core.Ai;

/// <summary>
/// A reasoning backend HAMMOR can route a turn to. Claude is the primary
/// implementation for v1; everything in Core talks to this interface so
/// another provider can be added without touching the agent loop.
/// </summary>
public interface IAiProvider
{
    /// <summary>
    /// Stable id matched against <see cref="Configuration.AiSettings.PrimaryProvider"/>.
    /// </summary>
    string ProviderId { get; }

    /// <summary>Human-readable name for the UI, e.g. "Claude".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Reports whether credentials are present and the provider is usable.
    /// Must not throw and must never include the credential in its result.
    /// </summary>
    Task<ProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a turn and returns the complete reply.</summary>
    /// <exception cref="AiProviderException">
    /// The request reached the provider and failed, or the provider is not
    /// configured. Callers surface the message; it is already redacted.
    /// </exception>
    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a configuration/connectivity probe.
/// </summary>
/// <param name="State">Whether the provider can currently be used.</param>
/// <param name="Detail">
/// Short explanation for the UI. Never contains credentials.
/// </param>
public sealed record ProviderAvailability(ProviderState State, string Detail)
{
    public bool IsUsable => State == ProviderState.Ready;

    public static ProviderAvailability Ready(string detail) => new(ProviderState.Ready, detail);

    public static ProviderAvailability NotConfigured(string detail) =>
        new(ProviderState.NotConfigured, detail);

    public static ProviderAvailability Error(string detail) => new(ProviderState.Error, detail);
}

public enum ProviderState
{
    /// <summary>No credential stored. The UI must show "Not Configured".</summary>
    NotConfigured = 0,

    /// <summary>Credential present and the provider accepted it.</summary>
    Ready = 1,

    /// <summary>Configured but failing, e.g. rejected key or no network.</summary>
    Error = 2,
}

/// <summary>Raised when a provider call fails in a way the caller must see.</summary>
public sealed class AiProviderException : Exception
{
    public AiProviderException(string message) : base(message)
    {
    }

    public AiProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
