using HAMMOR.Core.Ai;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Status;
using HAMMOR.Core.Tasks;
using HAMMOR.Core.Voice;
using Microsoft.Extensions.Logging;

namespace HAMMOR.Infrastructure.Status;

/// <summary>
/// Probes providers and devices on demand and publishes the result.
/// </summary>
/// <remarks>
/// Event-driven: nothing is polled. The shell refreshes on launch, after a
/// settings change, and when the user asks — which keeps HAMMOR's idle cost at
/// zero while still reflecting reality.
/// </remarks>
public sealed class SystemStatusService(
    IEnumerable<IAiProvider> aiProviders,
    IEnumerable<ITextToSpeechProvider> ttsProviders,
    IEnumerable<ISpeechToTextProvider> sttProviders,
    IAudioDeviceProvider audioDevices,
    ITaskStore taskStore,
    IConfigurationStore configurationStore,
    ILogger<SystemStatusService> logger) : ISystemStatusService
{
    private readonly IReadOnlyList<IAiProvider> _aiProviders = aiProviders?.ToList() ?? [];

    private readonly IReadOnlyList<ITextToSpeechProvider> _ttsProviders =
        ttsProviders?.ToList() ?? [];

    private readonly IReadOnlyList<ISpeechToTextProvider> _sttProviders =
        sttProviders?.ToList() ?? [];

    private readonly IAudioDeviceProvider _audioDevices =
        audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));

    private readonly ITaskStore _taskStore =
        taskStore ?? throw new ArgumentNullException(nameof(taskStore));

    private readonly IConfigurationStore _configurationStore =
        configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));

    private readonly ILogger<SystemStatusService> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private bool _isBusy;

    public SystemStatus Current { get; private set; } = SystemStatus.Unknown;

    public event EventHandler<SystemStatus>? StatusChanged;

    public async Task<SystemStatus> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var config = _configurationStore.Current;

        var claude = await ProbeAsync(
            _aiProviders.FirstOrDefault(p => Matches(p.ProviderId, config.Ai.PrimaryProvider)),
            p => p.CheckAvailabilityAsync(cancellationToken),
            $"AI provider '{config.Ai.PrimaryProvider}' is not registered.")
            .ConfigureAwait(false);

        var tts = await ProbeAsync(
            _ttsProviders.FirstOrDefault(p => Matches(p.ProviderId, config.Voice.TtsProvider)),
            p => p.CheckAvailabilityAsync(cancellationToken),
            $"TTS provider '{config.Voice.TtsProvider}' is not registered.")
            .ConfigureAwait(false);

        var stt = await ProbeAsync(
            _sttProviders.FirstOrDefault(p => Matches(p.ProviderId, config.Voice.SttProvider)),
            p => p.CheckAvailabilityAsync(cancellationToken),
            $"STT provider '{config.Voice.SttProvider}' is not registered.")
            .ConfigureAwait(false);

        var hasMicrophone = SafeDeviceCheck(() => _audioDevices.GetInputDevices().Count > 0);
        var hasSpeaker = SafeDeviceCheck(() => _audioDevices.GetOutputDevices().Count > 0);

        var runningTasks = await CountRunningTasksAsync(cancellationToken).ConfigureAwait(false);

        Current = new SystemStatus(
            claude, tts, stt, hasMicrophone, hasSpeaker, _isBusy, runningTasks);

        StatusChanged?.Invoke(this, Current);
        return Current;
    }

    public void SetBusy(bool isBusy)
    {
        if (_isBusy == isBusy)
        {
            return;
        }

        _isBusy = isBusy;
        Current = Current with { IsBusy = isBusy };
        StatusChanged?.Invoke(this, Current);
    }

    private static bool Matches(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private async Task<ProviderAvailability> ProbeAsync<T>(
        T? provider,
        Func<T, Task<ProviderAvailability>> probe,
        string missingMessage)
        where T : class
    {
        if (provider is null)
        {
            return ProviderAvailability.NotConfigured(missingMessage);
        }

        try
        {
            return await probe(provider).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider's own availability check throwing is a provider bug.
            // Report it as an error state rather than letting it break the
            // whole status refresh.
            _logger.LogError(ex, "Availability probe threw for {Provider}.", typeof(T).Name);
            return ProviderAvailability.Error($"Availability check failed: {ex.Message}");
        }
    }

    private bool SafeDeviceCheck(Func<bool> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio device enumeration failed.");
            return false;
        }
    }

    private async Task<int> CountRunningTasksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var running = await _taskStore
                .ListAsync([TaskState.Running], limit: 1000, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return running.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not count running tasks.");
            return 0;
        }
    }
}
