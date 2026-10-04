using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using HAMMOR.App.ViewModels;

namespace HAMMOR.App.Views;

/// <summary>Chat page: the main conversation surface.</summary>
public partial class ChatPage : Page
{
    private readonly ChatViewModel _viewModel;

    public ChatPage(ChatViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        DataContext = viewModel;
        InitializeComponent();

        // Keep the newest message in view as the transcript grows.
        _viewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add)
        {
            TranscriptScroll.ScrollToEnd();
        }
    }

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
