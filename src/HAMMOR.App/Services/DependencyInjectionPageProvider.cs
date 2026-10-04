using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Abstractions;

namespace HAMMOR.App.Services;

/// <summary>
/// Resolves navigation target pages from the DI container so pages receive
/// their view models by constructor injection.
/// </summary>
public sealed class DependencyInjectionPageProvider(IServiceProvider services)
    : INavigationViewPageProvider
{
    private readonly IServiceProvider _services =
        services ?? throw new ArgumentNullException(nameof(services));

    public object? GetPage(Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        // GetService (not GetRequiredService): NavigationView probes types,
        // and returning null lets it report an unregistered page rather than
        // throwing out of a navigation event.
        return _services.GetService(pageType);
    }
}
