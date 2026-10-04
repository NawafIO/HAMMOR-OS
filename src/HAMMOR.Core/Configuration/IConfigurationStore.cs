namespace HAMMOR.Core.Configuration;

/// <summary>
/// Loads and persists <see cref="HammorConfiguration"/> and notifies the app
/// when it changes.
/// </summary>
public interface IConfigurationStore
{
    /// <summary>The configuration currently in effect.</summary>
    HammorConfiguration Current { get; }

    /// <summary>Absolute path of the backing file, for display in Settings.</summary>
    string ConfigurationFilePath { get; }

    /// <summary>Reads from disk, or returns defaults when no file exists yet.</summary>
    Task<HammorConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="configuration"/>, replaces
    /// <see cref="Current"/>, then raises <see cref="ConfigurationChanged"/>.
    /// </summary>
    Task SaveAsync(HammorConfiguration configuration, CancellationToken cancellationToken = default);

    event EventHandler<HammorConfiguration>? ConfigurationChanged;
}
