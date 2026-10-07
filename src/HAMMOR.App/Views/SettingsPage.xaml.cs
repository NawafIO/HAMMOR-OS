using System.Windows;
using System.Windows.Controls;
using HAMMOR.App.Localization;
using HAMMOR.App.Presence;
using HAMMOR.App.ViewModels;

namespace HAMMOR.App.Views;

/// <summary>Settings page.</summary>
/// <remarks>
/// The two API-key fields are handled in code-behind rather than by binding.
/// WPF-UI's PasswordBox exposes the secret through <c>Password</c>, and
/// two-way binding a credential into a view-model property would leave it
/// sitting in the binding engine's state. Pushing it across on change keeps
/// the value in exactly one place, and the view model clears it as soon as the
/// key is written to DPAPI.
/// </remarks>
public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        DataContext = viewModel;
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            ShowMotionStatus();
            await viewModel.LoadAsync().ConfigureAwait(true);

            // Any text left in the boxes after a save/clear round-trip is
            // stale; wipe it so a key is never left visible on the page.
            AnthropicKeyBox.Password = string.Empty;
            ElevenLabsKeyBox.Password = string.Empty;
        };
    }

    /// <summary>Whether the Living Core moves fully or is held still by Windows' reduced motion.</summary>
    private void ShowMotionStatus() =>
        MotionStatusText.Text = LocalizationSource.Instance[
            LivingCoreMotionStatus.KeyFor(LivingCore.SystemPrefersReducedMotion)];

    /// <summary>Back to the main HAMMOR navigation: the page Settings was opened from.</summary>
    private void OnBackClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.LeaveSettings();

    private void OnOpenTasksClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.NavigateTo(typeof(TasksPage));

    /// <summary>Each category starts at its top.</summary>
    /// <remarks>
    /// The list's first selection arrives while the page is still being
    /// built, before the scroller (later in the markup) exists.
    /// </remarks>
    private void OnSectionChanged(object sender, SelectionChangedEventArgs e) =>
        ContentScroller?.ScrollToTop();

    private void OnAnthropicKeyChanged(object sender, RoutedEventArgs e) =>
        _viewModel.AnthropicKeyInput = AnthropicKeyBox.Password;

    private void OnElevenLabsKeyChanged(object sender, RoutedEventArgs e) =>
        _viewModel.ElevenLabsKeyInput = ElevenLabsKeyBox.Password;
}
