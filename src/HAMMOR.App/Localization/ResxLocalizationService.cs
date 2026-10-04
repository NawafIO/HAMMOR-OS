using System.ComponentModel;
using System.Globalization;
using System.Resources;
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

    private CultureInfo _culture;

    public ResxLocalizationService(
        IConfigurationStore configurationStore,
        ILogger<ResxLocalizationService> logger)
    {
        _configurationStore = configurationStore
                              ?? throw new ArgumentNullException(nameof(configurationStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

        _culture = target;
        ApplyCultureToThread(target);

        // Persist so the choice survives a restart.
        var updated = _configurationStore.Current.Clone();
        updated.General.Language = target.TwoLetterISOLanguageName;
        await _configurationStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("UI language switched to {Language}.", target.Name);

        RaiseAllStringsChanged();
        LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(CurrentLanguage, IsRightToLeft));
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
