using System.Windows;
using HAMMOR.App.Localization;
using HAMMOR.Core.Permissions;
using Wpf.Ui.Controls;

namespace HAMMOR.App.Views;

/// <summary>
/// Modal approval prompt for a privileged tool call.
/// </summary>
/// <remarks>
/// Deliberately dumb: it presents the request and reports Allow or Deny. The
/// decision is interpreted by <see cref="PermissionEvaluator"/>, and anything
/// other than pressing Allow — Deny, Escape, closing the window — leaves
/// <see cref="Window.DialogResult"/> false.
/// </remarks>
public partial class ConfirmationDialog : FluentWindow
{
    public ConfirmationDialog(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ToolName = request.ToolName;
        Summary = request.Summary;
        Details = request.Details;

        // Permission names are translated like every other enum in the UI.
        PermissionLabel = LocalizationSource.Instance[$"Permission.{request.Permission}"];

        DataContext = this;
        InitializeComponent();

        // Focus Deny so a stray Enter press cannot grant a privileged action.
        Loaded += (_, _) => DenyButton.Focus();
    }

    public string ToolName { get; }

    public string Summary { get; }

    public string Details { get; }

    public string PermissionLabel { get; }

    private void OnAllow(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnDeny(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
