using System.Windows;
using HAMMOR.App.ViewModels;
using Wpf.Ui.Controls;

namespace HAMMOR.App.Views;

/// <summary>First-run setup wizard.</summary>
/// <remarks>
/// Shown modally before the shell when
/// <c>HammorConfiguration.SetupCompleted</c> is false. API keys are read from
/// the PasswordBox controls in code-behind for the same reason as the settings
/// page: a credential should not be parked in a bound property.
/// </remarks>
public partial class FirstRunWindow : FluentWindow
{
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        DataContext = this;
        InitializeComponent();

        // The view model owns the decision that setup finished; the window
        // just reports it back to App via DialogResult.
        ViewModel.CompletionRequested += OnCompletionRequested;
    }

    public FirstRunViewModel ViewModel { get; }

    private void OnCompletionRequested(object? sender, bool completed)
    {
        DialogResult = completed;
        Close();
    }

    private void OnAnthropicKeyChanged(object sender, RoutedEventArgs e) =>
        ViewModel.AnthropicKeyInput = AnthropicKeyBox.Password;

    private void OnElevenLabsKeyChanged(object sender, RoutedEventArgs e) =>
        ViewModel.ElevenLabsKeyInput = ElevenLabsKeyBox.Password;

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.CompletionRequested -= OnCompletionRequested;
        base.OnClosed(e);
    }
}
