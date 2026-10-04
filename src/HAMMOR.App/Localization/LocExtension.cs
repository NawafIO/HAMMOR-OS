using System.Windows.Data;
using System.Windows.Markup;

namespace HAMMOR.App.Localization;

/// <summary>
/// XAML markup extension for localised text: <c>{loc:Loc Nav.Chat}</c>.
/// </summary>
/// <remarks>
/// Returns a live <see cref="Binding"/> rather than a resolved string, which
/// is what makes runtime language switching work: when
/// <see cref="LocalizationSource"/> raises a change notification, every
/// binding produced here re-reads its value in place. A markup extension that
/// returned a plain string would be evaluated once at load time and could not
/// be refreshed.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    /// <summary>Resource key, e.g. <c>Nav.Settings</c>.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
