using System.Windows;
using System.Windows.Controls;

namespace HAMMOR.App.Controls;

/// <summary>
/// One setting: its name and an optional one-line explanation at the start
/// of the row, its control at the end. With <see cref="IsStacked"/> the
/// control sits under the text instead, for wide content such as a key field
/// or the Claude Code panel.
/// </summary>
/// <remarks>
/// Rows follow the flow direction, so in Arabic the text is on the right and
/// the control on the left. The look lives in Themes/Settings.xaml.
/// </remarks>
public sealed class SettingsRow : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(SettingsRow),
        new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingsRow),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsStackedProperty = DependencyProperty.Register(
        nameof(IsStacked),
        typeof(bool),
        typeof(SettingsRow),
        new PropertyMetadata(false));

    /// <summary>The setting's name.</summary>
    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>An optional explanation under the name.</summary>
    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>True to put the control under the text, full width.</summary>
    public bool IsStacked
    {
        get => (bool)GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }
}
