using System.Windows;
using HAMMOR.App.Localization;
using HAMMOR.App.Views;
using HAMMOR.Core.Permissions;

namespace HAMMOR.App.Services;

/// <summary>
/// Shows a modal dialog asking the user to approve a privileged tool call.
/// </summary>
/// <remarks>
/// The UI implementation of the Core authorisation contract. Anything other
/// than an explicit Allow — Deny, Escape, closing the window — resolves to
/// false, so a dismissed prompt can never be read as consent.
/// </remarks>
public sealed class DialogConfirmationService : IConfirmationService
{
    public Task<bool> RequestApprovalAsync(
        ConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        var application = Application.Current;

        if (application is null)
        {
            // No UI thread (unit tests, headless host). Denying is the safe
            // default: never auto-approve because no one can be asked.
            return Task.FromResult(false);
        }

        return application.Dispatcher.InvokeAsync(() =>
        {
            // Owned by the active window (for example the task editor), so the
            // prompt opens centred over the form that asked for it.
            var owner = application.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? application.MainWindow;

            var dialog = new ConfirmationDialog(request)
            {
                Owner = owner,
                FlowDirection = LocalizationSource.Instance.FlowDirection,
            };

            return dialog.ShowDialog() == true;
        }).Task;
    }
}
