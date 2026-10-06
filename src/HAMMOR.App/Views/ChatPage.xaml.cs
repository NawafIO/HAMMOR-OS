using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HAMMOR.App.Presence;
using HAMMOR.App.ViewModels;

namespace HAMMOR.App.Views;

/// <summary>Chat page: the main conversation surface.</summary>
public partial class ChatPage : Page
{
    private readonly ChatViewModel _viewModel;
    private readonly LivingCorePresenter _presence;

    public ChatPage(ChatViewModel viewModel, LivingCorePresenter presence)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));

        DataContext = viewModel;
        InitializeComponent();

        // Both Living Core instances show the presenter's single state; only
        // the visible one draws.
        HeroCore.DataContext = presence;
        CompactCore.DataContext = presence;

        // Keep the newest message in view as the transcript grows.
        _viewModel.Messages.CollectionChanged += OnMessagesChanged;

        // Listening (P0): the composer is the attention signal.
        InputBox.IsKeyboardFocusWithinChanged += OnComposerFocusChanged;
        Unloaded += OnUnloaded;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add)
        {
            TranscriptScroll.ScrollToEnd();
        }
    }

    private void OnComposerFocusChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _presence.SetComposerFocused(e.NewValue is true);

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        _presence.SetComposerFocused(false);

    /// <summary>
    /// Enter sends; Shift+Enter inserts a newline. The box accepts returns so
    /// multi-line input is possible, which is why this has to be explicit.
    /// </summary>
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            return;
        }

        e.Handled = true;

        if (_viewModel.SendCommand.CanExecute(null))
        {
            _viewModel.SendCommand.Execute(null);
        }
    }
}
