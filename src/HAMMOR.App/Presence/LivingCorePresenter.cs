using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HAMMOR.App.Localization;
using HAMMOR.App.ViewModels;
using HAMMOR.Core.Localization;
using HAMMOR.Core.Tasks;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.Presence;

/// <summary>
/// Maps what HAMMOR is doing to the one state the Living Core shows. The app
/// tells this class facts; the control decides how they look.
/// </summary>
/// <remarks>
/// <para>
/// Observation only: it subscribes to existing events and never changes chat,
/// voice or task behaviour. Sources:
/// </para>
/// <list type="bullet">
/// <item><b>Speaking</b>: real playback from <see cref="SpeechPlaybackMonitor"/>,
/// held 0.8 s after the last word (Interaction board).</item>
/// <item><b>Listening</b>: P0 uses the composer, attention while the user types
/// in it, settling 1.2 s after they stop. No microphone is opened; a future
/// voice source sets the same signal.</item>
/// <item><b>Thinking</b>: a chat turn is in flight
/// (<see cref="ChatViewModel.IsBusy"/>).</item>
/// <item><b>Blocked</b>: any task in <see cref="TaskState.Blocked"/>, read at
/// start and kept current from <see cref="ITaskStore.TaskChanged"/>.</item>
/// <item><b>Error</b>: a failed chat turn, or a task that failed during this
/// session. It holds until the user acts (sends again, clears the
/// conversation, or opens Tasks), never on a timer.</item>
/// </list>
/// </remarks>
public sealed partial class LivingCorePresenter : ObservableObject, IDisposable
{
    // "Idle 0.8 s after the last word" and "settles 1.2 s after silence".
    private static readonly TimeSpan SpeakingRelease = TimeSpan.FromSeconds(0.8);
    private static readonly TimeSpan ListeningRelease = TimeSpan.FromSeconds(1.2);

    private readonly ChatViewModel _chat;
    private readonly ITaskStore _taskStore;
    private readonly SpeechPlaybackMonitor _playback;
    private readonly ILocalizationService _localization;
    private readonly ILogger<LivingCorePresenter> _logger;
    private readonly Dispatcher _dispatcher;

    private readonly HashSet<string> _blockedTasks = new(StringComparer.Ordinal);
    private readonly List<HammorTask> _changesDuringLoad = [];

    // One-shot release timers: they run only for the hold after speech or
    // typing ends, never as a background poll.
    private readonly DispatcherTimer _speakingRelease;
    private readonly DispatcherTimer _listeningRelease;

    private bool _loadingTasks;
    private bool _speaking;
    private bool _listening;
    private bool _composerFocused;
    private bool _chatError;
    private bool _taskFailure;
    private bool _taskListVisible;
    private bool _disposed;

    public LivingCorePresenter(
        ChatViewModel chat,
        ITaskStore taskStore,
        SpeechPlaybackMonitor playback,
        ILocalizationService localization,
        ILogger<LivingCorePresenter> logger)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _speakingRelease = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = SpeakingRelease };
        _speakingRelease.Tick += OnSpeakingReleaseTick;
        _listeningRelease = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = ListeningRelease };
        _listeningRelease.Tick += OnListeningReleaseTick;

        _chat.PropertyChanged += OnChatPropertyChanged;
        _chat.Messages.CollectionChanged += OnMessagesChanged;
        _taskStore.TaskChanged += OnTaskChanged;
        _playback.PlaybackChanged += OnPlaybackChanged;
        _localization.LanguageChanged += OnLanguageChanged;

        _ = LoadBlockedTasksAsync();
        Resolve();
    }

    /// <summary>The single state the Living Core shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private LivingCoreState _state = LivingCoreState.Idle;

    /// <summary>
    /// What the core is showing, in words: its accessible name and tooltip, so
    /// the state is clear with animation off or without sight.
    /// </summary>
    public string Description => LocalizationSource.Instance[DescriptionKey(State)];

    /// <summary>
    /// The chat page reports keyboard focus entering or leaving the composer.
    /// </summary>
    public void SetComposerFocused(bool focused)
    {
        _composerFocused = focused;
        SyncListening();
    }

    /// <summary>
    /// The shell reports whether the Tasks page is on screen. A failed task is
    /// seen there, which counts as the user acting on it.
    /// </summary>
    public void SetTaskListVisible(bool visible)
    {
        _taskListVisible = visible;
        if (visible && _taskFailure)
        {
            _taskFailure = false;
            Resolve();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _speakingRelease.Stop();
        _listeningRelease.Stop();
        _speakingRelease.Tick -= OnSpeakingReleaseTick;
        _listeningRelease.Tick -= OnListeningReleaseTick;
        _chat.PropertyChanged -= OnChatPropertyChanged;
        _chat.Messages.CollectionChanged -= OnMessagesChanged;
        _taskStore.TaskChanged -= OnTaskChanged;
        _playback.PlaybackChanged -= OnPlaybackChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private static string DescriptionKey(LivingCoreState state) => state switch
    {
        LivingCoreState.Listening => "LivingCore.Listening",
        LivingCoreState.Thinking => "LivingCore.Thinking",
        LivingCoreState.Speaking => "LivingCore.Speaking",
        LivingCoreState.Blocked => "LivingCore.Blocked",
        LivingCoreState.Error => "LivingCore.Error",
        _ => "LivingCore.Idle",
    };

    private void Resolve()
    {
        if (_disposed)
        {
            return;
        }

        State = LivingCoreStateResolver.Resolve(new LivingCoreSignals(
            IsSpeaking: _speaking,
            IsListening: _listening,
            IsThinking: _chat.IsBusy,
            IsBlocked: _blockedTasks.Count > 0,
            HasError: _chatError || _taskFailure));
    }

    // ---- Speaking: real playback ----

    private void OnPlaybackChanged(object? sender, bool isPlaying) => OnUiThread(SyncSpeaking);

    private void SyncSpeaking()
    {
        if (_disposed)
        {
            return;
        }

        // Read the live count rather than the notification: two utterances
        // can overlap for a moment when one replaces the other.
        if (_playback.IsSpeaking)
        {
            _speakingRelease.Stop();
            _speaking = true;
        }
        else if (_speaking && !_speakingRelease.IsEnabled)
        {
            _speakingRelease.Start();
        }

        Resolve();
    }

    private void OnSpeakingReleaseTick(object? sender, EventArgs e)
    {
        _speakingRelease.Stop();
        if (!_playback.IsSpeaking)
        {
            _speaking = false;
        }

        Resolve();
    }

    // ---- Listening: attention while the user types (P0) ----

    private bool IsAttending => _composerFocused && !string.IsNullOrWhiteSpace(_chat.Input);

    private void SyncListening()
    {
        if (_disposed)
        {
            return;
        }

        if (IsAttending)
        {
            _listeningRelease.Stop();
            _listening = true;
        }
        else if (_listening && !_listeningRelease.IsEnabled)
        {
            _listeningRelease.Start();
        }

        Resolve();
    }

    private void OnListeningReleaseTick(object? sender, EventArgs e)
    {
        _listeningRelease.Stop();
        if (!IsAttending)
        {
            _listening = false;
        }

        Resolve();
    }

    /// <summary>
    /// A message was sent: the user has finished addressing HAMMOR, so
    /// Thinking takes over at once instead of after the settle time.
    /// </summary>
    private void EndListening()
    {
        _listeningRelease.Stop();
        _listening = false;
    }

    // ---- Chat: busy, sends, failures ----

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatViewModel.IsBusy):
                if (_chat.IsBusy)
                {
                    EndListening();
                }

                Resolve();
                break;

            case nameof(ChatViewModel.Input):
                SyncListening();
                break;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (var item in e.NewItems)
                {
                    if (item is not ChatMessageViewModel message)
                    {
                        continue;
                    }

                    if (message.IsError)
                    {
                        _chatError = true;
                    }
                    else if (message.IsUser)
                    {
                        // Sending again is acting on an earlier failure.
                        _chatError = false;
                        EndListening();
                    }
                }

                break;

            case NotifyCollectionChangedAction.Reset:
                // The conversation was cleared.
                _chatError = false;
                break;
        }

        Resolve();
    }

    // ---- Tasks: Blocked and in-session failures ----

    private async Task LoadBlockedTasksAsync()
    {
        _loadingTasks = true;

        try
        {
            var blocked = await _taskStore.ListAsync([TaskState.Blocked]).ConfigureAwait(true);
            foreach (var task in blocked)
            {
                if (task.State == TaskState.Blocked)
                {
                    _blockedTasks.Add(task.Id);
                }
            }
        }
        catch (Exception ex)
        {
            // The core then simply does not show Blocked for tasks that were
            // already waiting; the Tasks page still lists them.
            _logger.LogWarning(ex, "Could not read blocked tasks for the Living Core.");
        }
        finally
        {
            _loadingTasks = false;

            // Changes that arrived while reading are newer than the snapshot.
            foreach (var change in _changesDuringLoad)
            {
                ApplyTaskChange(change);
            }

            _changesDuringLoad.Clear();
            Resolve();
        }
    }

    private void OnTaskChanged(object? sender, HammorTask task) => OnUiThread(() => HandleTaskChange(task));

    private void HandleTaskChange(HammorTask task)
    {
        if (_disposed)
        {
            return;
        }

        if (_loadingTasks)
        {
            _changesDuringLoad.Add(task);
            return;
        }

        ApplyTaskChange(task);
        Resolve();
    }

    private void ApplyTaskChange(HammorTask task)
    {
        if (task.State == TaskState.Blocked)
        {
            _blockedTasks.Add(task.Id);
        }
        else
        {
            _blockedTasks.Remove(task.Id);
        }

        // A task failing now needs the user until they look at it. Failures
        // already on screen in Tasks are seen as they happen.
        if (task.State == TaskState.Failed && !_taskListVisible)
        {
            _taskFailure = true;
        }
    }

    // ---- Plumbing ----

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e) =>
        OnPropertyChanged(nameof(Description));

    private void OnUiThread(Action action)
    {
        if (_dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            // Never blocks the caller (the scheduler or the audio thread).
            _ = _dispatcher.InvokeAsync(action);
        }
    }
}
