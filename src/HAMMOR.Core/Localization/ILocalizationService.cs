using System.ComponentModel;

namespace HAMMOR.Core.Localization;

/// <summary>
/// Resolves UI strings for the active language and signals when that language
/// changes so bound views can re-read every string without being rebuilt.
/// </summary>
/// <remarks>
/// The indexer is deliberately the only lookup surface: it lets a XAML binding
/// target <c>[SomeKey]</c> and refresh on a single
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> notification with an
/// empty property name. Using <c>x:Static</c> against generated resx
/// properties cannot do that — those are resolved once at load time — which is
/// why nothing in the UI is allowed to reference resx types directly.
/// </remarks>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>Localised string for <paramref name="key"/>.</summary>
    /// <remarks>
    /// Never throws and never returns null: an unknown key comes back as
    /// <c>!key!</c> so a missing translation is visible in the UI during
    /// development instead of crashing or silently rendering blank.
    /// </remarks>
    string this[string key] { get; }

    /// <summary>BCP-47 code of the active language, e.g. <c>en</c> or <c>ar</c>.</summary>
    string CurrentLanguage { get; }

    /// <summary>True when the active language reads right-to-left.</summary>
    bool IsRightToLeft { get; }

    /// <summary>Languages this build ships translations for.</summary>
    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    /// <summary>
    /// Switches language and persists the choice. Raises
    /// <see cref="LanguageChanged"/> plus a blanket PropertyChanged so bound
    /// strings and flow direction update without an application restart.
    /// </summary>
    Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default);

    /// <summary>Raised after the active language has changed.</summary>
    event EventHandler<LanguageChangedEventArgs>? LanguageChanged;
}

/// <summary>A selectable UI language.</summary>
/// <param name="Code">BCP-47 code, e.g. <c>ar</c>.</param>
/// <param name="NativeName">Name written in the language itself, e.g. العربية.</param>
/// <param name="IsRightToLeft">Whether the script reads right-to-left.</param>
public sealed record LanguageOption(string Code, string NativeName, bool IsRightToLeft);

public sealed class LanguageChangedEventArgs(string languageCode, bool isRightToLeft) : EventArgs
{
    public string LanguageCode { get; } = languageCode;

    public bool IsRightToLeft { get; } = isRightToLeft;
}
