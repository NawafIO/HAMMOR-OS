using System.Windows;
using HAMMOR.App.Views;

namespace HAMMOR.App.Presence;

/// <summary>
/// Notices HAMMOR's approval prompt (<see cref="ConfirmationDialog"/>) opening
/// and closing, so the Living Core can show Warning while a risky action waits
/// for the user.
/// </summary>
/// <remarks>
/// Observation only. It hears the dialog's own Loaded and Closed events and
/// nothing else: it never shows, answers or closes a prompt, never reads what
/// it asks, and the confirmation service, its decision and its registration
/// are untouched.
/// </remarks>
internal static class ApprovalPromptWatcher
{
    private static bool _registered;
    private static int _open;

    /// <summary>Raised on the UI thread when a prompt opens or closes.</summary>
    public static event EventHandler? Changed;

    /// <summary>True while at least one approval prompt is open.</summary>
    public static bool IsOpen => _open > 0;

    /// <summary>Starts listening. Safe to call more than once; call on the UI thread.</summary>
    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(
            typeof(ConfirmationDialog),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnPromptLoaded));
    }

    private static void OnPromptLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window prompt)
        {
            return;
        }

        _open++;
        prompt.Closed += OnPromptClosed;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void OnPromptClosed(object? sender, EventArgs e)
    {
        if (sender is Window prompt)
        {
            prompt.Closed -= OnPromptClosed;
        }

        _open = Math.Max(0, _open - 1);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
