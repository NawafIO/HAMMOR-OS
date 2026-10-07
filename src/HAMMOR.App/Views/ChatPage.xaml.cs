using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using HAMMOR.App.Localization;
using HAMMOR.App.Presence;
using HAMMOR.App.ViewModels;

namespace HAMMOR.App.Views;

/// <summary>Chat page: the Living Home and the conversation surface.</summary>
public partial class ChatPage : Page
{
    /// <summary>
    /// The hero never draws larger than the canvas's own hero (640 px for
    /// the 200-unit design box, 3.2 px per unit): every stroke and glow stays
    /// at or below the scale the approved artwork was drawn at.
    /// </summary>
    internal const double MaxHeroSize = 640.0;

    /// <summary>Small windows scale the hero down rather than crop it.</summary>
    internal const double MinHeroSize = 180.0;

    /// <summary>The hero takes at most this share of the stage's width.</summary>
    internal const double HeroWidthShare = 0.62;

    /// <summary>The page light reaches this far, relative to the core.</summary>
    internal const double AmbientScale = 2.3;

    /// <summary>
    /// The greeting rises this share of the core into its faint lower aura,
    /// so the text reads with the core rather than floating below it.
    /// </summary>
    internal const double GreetingLift = 0.05;

    private readonly ChatViewModel _viewModel;
    private readonly LivingCorePresenter _presence;
    private bool _subscribed;

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

        // Listening (P0): the composer is the attention signal.
        InputBox.IsKeyboardFocusWithinChanged += OnComposerFocusChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Side of the hero core for a stage of the given size: as large as the
    /// stage allows, within <see cref="MinHeroSize"/> and
    /// <see cref="MaxHeroSize"/>, on whole pixels.
    /// </summary>
    internal static double HeroSizeFor(double stageWidth, double stageHeight)
    {
        if (double.IsNaN(stageWidth) || double.IsNaN(stageHeight) || stageWidth <= 0.0 || stageHeight <= 0.0)
        {
            return MinHeroSize;
        }

        var side = Math.Min(stageWidth * HeroWidthShare, stageHeight);
        return Math.Floor(Math.Clamp(side, MinHeroSize, MaxHeroSize));
    }

    /// <summary>The greeting for the local hour: morning, afternoon or evening.</summary>
    internal static string GreetingKeyFor(int hour) => hour switch
    {
        >= 5 and < 12 => "Home.Greeting.Morning",
        >= 12 and < 18 => "Home.Greeting.Afternoon",
        _ => "Home.Greeting.Evening",
    };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            // Keep the newest message in view as the transcript grows. Pages
            // are rebuilt on navigation; subscribing only while loaded keeps an
            // old page from being held by the singleton view model.
            _viewModel.Messages.CollectionChanged += OnMessagesChanged;
            _subscribed = true;
        }

        // A live binding, so a language switch while the home is open
        // re-reads the greeting in the new language.
        GreetingText.SetBinding(
            TextBlock.TextProperty,
            new Binding($"[{GreetingKeyFor(DateTime.Now.Hour)}]")
            {
                Source = LocalizationSource.Instance,
                Mode = BindingMode.OneWay,
            });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _viewModel.Messages.CollectionChanged -= OnMessagesChanged;
            _subscribed = false;
        }

        _presence.SetComposerFocused(false);
    }

    /// <summary>
    /// The Living Core size strategy: the hero follows the space it is given
    /// and is drawn at that exact size (vector, never a scaled bitmap), so its
    /// geometry, strokes and glows keep the canvas's proportions.
    /// </summary>
    private void OnHeroStageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var side = HeroSizeFor(e.NewSize.Width, e.NewSize.Height);
        HeroCore.Width = side;
        HeroCore.Height = side;

        // Centre the page light on the core and let it reach past the stage:
        // negative margins enlarge its slot instead of clipping it.
        var reach = side * AmbientScale;
        var horizontal = (e.NewSize.Width - reach) / 2.0;
        var vertical = (e.NewSize.Height - reach) / 2.0;
        HeroAmbient.Margin = new Thickness(horizontal, vertical, horizontal, vertical);

        HomeTextShift.Y = -Math.Round(side * GreetingLift);
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add)
        {
            TranscriptScroll.ScrollToEnd();
        }
    }

    /// <summary>Copies one message's text.</summary>
    private void OnCopyMessageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ChatMessageViewModel message)
        {
            return;
        }

        try
        {
            Clipboard.SetText(message.Text);
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard; copying again will work.
        }
    }

    private void OnComposerFocusChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _presence.SetComposerFocused(e.NewValue is true);

    /// <summary>
    /// Enter sends; Shift+Enter inserts a newline. Handled on the preview
    /// event because a box that accepts returns consumes Enter itself.
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
