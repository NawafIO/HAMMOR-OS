using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Threading;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Localization;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.Localization;

/// <summary>
/// Resx-backed <see cref="ILocalizationService"/> that supports switching
/// language at runtime.
/// </summary>
/// <remarks>
/// Lookups go through the indexer and are resolved against the current
/// <see cref="CultureInfo"/> on every read, so raising
/// <see cref="PropertyChanged"/> with an empty property name is enough to make
/// every bound string in the UI re-read itself. This is why the views bind via
/// <see cref="LocExtension"/> instead of using <c>x:Static</c> against the
/// generated resx designer class — the latter resolves once at load time and
/// could not be refreshed without rebuilding the visual tree.
/// </remarks>
public sealed class ResxLocalizationService : ILocalizationService
{
    private static readonly LanguageOption[] Languages =
    [
        new("en", "English", IsRightToLeft: false),
        new("ar", "العربية", IsRightToLeft: true),
    ];

    private readonly ResourceManager _resources;
    private readonly IConfigurationStore _configurationStore;
    private readonly ILogger<ResxLocalizationService> _logger;

    // The UI thread's dispatcher. Language changes are announced on it,
    // because every listener updates WPF objects.
    private readonly Dispatcher? _dispatcher;

    private CultureInfo _culture;

    public ResxLocalizationService(
        IConfigurationStore configurationStore,
        ILogger<ResxLocalizationService> logger)
        : this(configurationStore, logger, Application.Current?.Dispatcher)
    {
    }

    /// <param name="dispatcher">
    /// The UI thread's dispatcher; <see langword="null"/> announces on the
    /// calling thread.
    /// </param>
    internal ResxLocalizationService(
        IConfigurationStore configurationStore,
        ILogger<ResxLocalizationService> logger,
        Dispatcher? dispatcher)
    {
        _configurationStore = configurationStore
                              ?? throw new ArgumentNullException(nameof(configurationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher;

        _resources = new ResourceManager(
            "HAMMOR.App.Localization.Strings", typeof(ResxLocalizationService).Assembly);

        var configured = configurationStore.Current.General.Language;
        _culture = ResolveCulture(configured);
        ApplyCultureToThread(_culture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            try
            {
                var value = _resources.GetString(key, _culture);

                // A missing key renders as !key! rather than blank or a crash,
                // so an untranslated string is obvious during development
                // instead of silently disappearing from the UI.
                return value ?? $"!{key}!";
            }
            catch (MissingManifestResourceException ex)
            {
                _logger.LogError(ex, "String resources could not be loaded for key '{Key}'.", key);
                return $"!{key}!";
            }
        }
    }

    public string CurrentLanguage => _culture.TwoLetterISOLanguageName;

    public bool IsRightToLeft =>
        Languages.FirstOrDefault(l => l.Code == CurrentLanguage)?.IsRightToLeft ?? false;

    public IReadOnlyList<LanguageOption> AvailableLanguages => Languages;

    public async Task SetLanguageAsync(
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var target = ResolveCulture(languageCode);

        if (string.Equals(target.Name, _culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Persist first, so the choice survives a restart and a choice that
        // cannot be saved changes nothing on screen.
        var updated = _configurationStore.Current.Clone();
        updated.General.Language = target.TwoLetterISOLanguageName;
        await _configurationStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        // The save completes on a thread-pool thread. The switch itself and its
        // announcements belong on the UI thread: listeners rebuild pages and
        // update bound WPF objects, and from any other thread WPF throws "The
        // calling thread cannot access this object because a different thread
        // owns it".
        await OnUiThreadAsync(() => Apply(target)).ConfigureAwait(false);
    }

    private void Apply(CultureInfo target)
    {
        _culture = target;
        ApplyCultureToThread(target);

        _logger.LogInformation("UI language switched to {Language}.", target.Name);

        RaiseAllStringsChanged();
        LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(CurrentLanguage, IsRightToLeft));
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread and completes when it
    /// has run; an exception it throws reaches the caller of
    /// <see cref="SetLanguageAsync"/>.
    /// </summary>
    private Task OnUiThreadAsync(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    /// <summary>
    /// Signals that every localised string must be re-read.
    /// </summary>
    /// <remarks>
    /// An empty property name is the WPF convention for "all properties
    /// changed"; <c>Item[]</c> is raised as well because that is the name WPF
    /// uses internally for indexer bindings, and some binding paths only
    /// listen for that form.
    /// </remarks>
    private void RaiseAllStringsChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>
    /// Sets the thread culture so date, number and string comparisons follow
    /// the chosen language too — not just the resource lookups.
    /// </summary>
    private static void ApplyCultureToThread(CultureInfo culture)
    {
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // CurrentCulture is deliberately left alone: formatting numbers and
        // dates with Arabic-Indic digits by default surprises users who expect
        // Windows' regional settings to win for data, while UI text follows
        // the app language.
    }

    /// <summary>
    /// Maps a language code to a supported culture, falling back to English
    /// for anything unrecognised.
    /// </summary>
    private CultureInfo ResolveCulture(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return CultureInfo.GetCultureInfo("en");
        }

        var primary = languageCode.Split('-')[0].ToLowerInvariant();

        if (Languages.All(l => l.Code != primary))
        {
            _logger.LogWarning(
                "Language '{Language}' is not supported; falling back to English.", languageCode);
            return CultureInfo.GetCultureInfo("en");
        }

        return CultureInfo.GetCultureInfo(primary);
    }
}
