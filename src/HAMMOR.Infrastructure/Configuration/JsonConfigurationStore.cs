using System.Text.Json;
using System.Text.Json.Serialization;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Storage;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Configuration;

/// <summary>
/// Persists <see cref="HammorConfiguration"/> as indented JSON next to the
/// database. The file is git-ignored and holds no credentials.
/// </summary>
public sealed class JsonConfigurationStore(
    HammorPaths paths,
    ILogger<JsonConfigurationStore> logger) : IConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly HammorPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private readonly ILogger<JsonConfigurationStore> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public HammorConfiguration Current { get; private set; } = new();

    public string ConfigurationFilePath => _paths.ConfigurationFile;

    public event EventHandler<HammorConfiguration>? ConfigurationChanged;

    public async Task<HammorConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.ConfigurationFile;

        if (!File.Exists(path))
        {
            _logger.LogInformation(
                "No configuration at {Path}; using defaults until first save.", path);
            Current = new HammorConfiguration();
            return Current;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer
                .DeserializeAsync<HammorConfiguration>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is null)
            {
                // An empty or `null` document is corrupt, not "no config".
                // Surfaced rather than silently replaced so the user can see
                // their settings did not load.
                _logger.LogError(
                    "Configuration at {Path} deserialised to null; falling back to defaults.",
                    path);
                Current = new HammorConfiguration();
                return Current;
            }

            Current = loaded;
            _logger.LogInformation("Loaded configuration from {Path}.", path);
            return Current;
        }
        catch (JsonException ex)
        {
            // Malformed JSON. Keep the bad file for inspection instead of
            // overwriting it, and continue on defaults so the app still starts.
            _logger.LogError(
                ex,
                "Configuration at {Path} is not valid JSON. Continuing with defaults; the "
                + "existing file has been left untouched for inspection.",
                path);

            Current = new HammorConfiguration();
            return Current;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Could not read configuration at {Path}.", path);
            Current = new HammorConfiguration();
            return Current;
        }
    }

    public async Task SaveAsync(
        HammorConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureCreated();

            // Write to a temp file then move, so an interrupted write cannot
            // leave a half-serialised config behind.
            var target = _paths.ConfigurationFile;
            var temp = target + ".tmp";

            await using (var stream = File.Create(temp))
            {
                await JsonSerializer
                    .SerializeAsync(stream, configuration, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temp, target, overwrite: true);

            Current = configuration;
            _logger.LogInformation("Saved configuration to {Path}.", target);
        }
        finally
        {
            _writeGate.Release();
        }

        ConfigurationChanged?.Invoke(this, configuration);
    }
}
