using System.Runtime.Versioning;
using HAMMOR.Core.Voice;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace HAMMOR.Platform.Windows.Audio;

/// <summary>
/// Enumerates Windows audio endpoints through WASAPI.
/// </summary>
/// <remarks>
/// Device enumeration is real and working in Phase 1 — it is what lets the UI
/// honestly report whether a microphone and speaker exist, independent of
/// whether speech recognition is implemented.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class NAudioDeviceProvider(ILogger<NAudioDeviceProvider> logger)
    : IAudioDeviceProvider
{
    private readonly ILogger<NAudioDeviceProvider> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public IReadOnlyList<AudioDevice> GetInputDevices() => Enumerate(DataFlow.Capture);

    public IReadOnlyList<AudioDevice> GetOutputDevices() => Enumerate(DataFlow.Render);

    private IReadOnlyList<AudioDevice> Enumerate(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            // Resolve the default endpoint id first so devices can be flagged
            // without a second enumeration pass.
            string? defaultId = null;
            if (enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia))
            {
                using var defaultDevice =
                    enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                defaultId = defaultDevice.ID;
            }

            var devices = new List<AudioDevice>();

            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new AudioDevice(
                        device.ID,
                        device.FriendlyName,
                        string.Equals(device.ID, defaultId, StringComparison.Ordinal)));
                }
            }

            return devices;
        }
        catch (Exception ex)
        {
            // A machine with no audio stack at all (some VMs, Server Core)
            // throws here. Reported as empty, and logged — the UI then shows
            // the device as unavailable rather than claiming one exists.
            _logger.LogWarning(
                ex, "Could not enumerate {Flow} audio devices.", flow);

            return [];
        }
    }
}
