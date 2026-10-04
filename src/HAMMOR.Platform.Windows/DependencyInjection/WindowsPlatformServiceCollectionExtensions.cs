using System.Runtime.Versioning;
using HAMMOR.Core.Security;
using HAMMOR.Core.Voice;
using HAMMOR.Platform.Windows.Audio;
using HAMMOR.Platform.Windows.Security;
using Microsoft.Extensions.DependencyInjection;

namespace HAMMOR.Platform.Windows.DependencyInjection;

/// <summary>
/// Registers the Windows-specific implementations of Core abstractions.
/// </summary>
public static class WindowsPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Adds DPAPI secret storage and WASAPI audio device enumeration and
    /// playback.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddHammorWindowsPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<IAudioDeviceProvider, NAudioDeviceProvider>();
        services.AddSingleton<IAudioPlayer, NAudioPlayer>();

        return services;
    }
}
