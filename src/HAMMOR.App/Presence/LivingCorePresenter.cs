using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
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
/// Maps what HAMMOR is doing to the one state the Living Core shows, and to the
/// reactions it plays. The app tells this class facts; the control decides how
/// they look.
/// </summary>
/// <remarks>
/// <para>
/// Observation only: it subscribes to existing events and never changes chat,
/// voice, task or approval behaviour. States:
/// </para>
/// <list type="bullet">
/// <item><b>Speaking</b>: real playback from <see cref="SpeechPlaybackMonitor"/>,
/// held 0.8 s after the last word (Interaction board).</item>
/// <item><b>Listening</b>: the user types in the focused composer, settling
/// 1.2 s after they stop. No microphone is opened.</item>
/// <item><b>Thinking</b>: a chat turn is in flight and its reply has not
/// arrived yet (<see cref="ChatViewModel.IsBusy"/>).</item>
/// <item><b>Warning</b>: an approval prompt for a risky action is open
/// (<see cref="ApprovalPromptWatcher"/>).</item>
/// <item><b>Blocked</b>: any task in <see cref="TaskState.Blocked"/>, read at
/// start and kept current from <see cref="ITaskStore.TaskChanged"/>.</item>
/// <item><b>Error</b>: a failed chat turn, or a task that failed during this
/// session. It holds until the user acts (sends again, clears the
/// conversation, or opens Tasks), never on a timer.</item>
/// <item><b>Success</b>: a task completed during this session, or the user
/// approved a blocked one; held for its 3.2 s timeline.</item>
/// <item><b>Sleep</b>: the window is minimised, or ten minutes passed without
/// the user touching HAMMOR.</item>
/// <item><b>Wake</b>: the window returns, or anything wakes it from Sleep; 2.6 s,
/// then the state underneath. The control plays the same ignition when the
/// app opens.</item>
/// </list>
/// <para>
/// Reactions (<see cref="Reaction"/>): a reply arriving (ripple and a glance
/// toward the transcript) and the navigation pane opening (a glance and the
/// aura leaning toward it).
/// </para>
/// </remarks>
public sealed partial class LivingCorePresenter : ObservableObject, IDisposable
{
    // "Idle 0.8 s after the last word" and "settles 1.2 s after silence".
    private static readonly TimeSpan SpeakingRelease = TimeSpan.FromSeconds(0.8);
    private static readonly TimeSpan ListeningRelease = TimeSpan.FromSeconds(1.2);

    /// <summary>Success reaches its peak once and settles: one timeline.</summary>
    internal static readonly TimeSpan SuccessHold = TimeSpan.FromSeconds(LivingCoreMotion.SuccessSeconds);

    /// <summary>The 2.4 s ignition plus a beat, then it hands over.</summary>
    internal static readonly TimeSpan WakeHold = TimeSpan.FromSeconds(LivingCoreMotion.WakeHoldSeconds);

    /// <summary>"Ten minutes without interaction" (state board 09).</summary>
    internal static readonly TimeSpan SleepAfter = TimeSpan.FromMinutes(10);

    private readonly ChatViewModel _chat;
    private readonly ITaskStore _taskStore;
    private readonly SpeechPlaybackMonitor _playback;
    private readonly ILocalizationService _localization;
    private readonly ILogger<LivingCorePresenter> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly HashSet<string> _blockedTasks = new(StringComparer.Ordinal);
    private readonly List<HammorTask> _changesDuringLoad = [];

    // One-shot timers: they run only for a hold, never as a background poll.
    // The sleep timer is armed once and re-armed only when it fires early.
    private readonly DispatcherTimer _speakingRelease;
    private readonly DispatcherTimer _listeningRelease;
    private readonly DispatcherTimer _successRelease;
    private readonly DispatcherTimer _wakeRelease;
    private readonly DispatcherTimer _sleepTimer;

    private bool _loadingTasks;
    private bool _speaking;
    private bool _listening;
    private bool _composerFocused;
    private bool _awaitingReply;
    private bool _confirming;
    private bool _chatError;
    private bool _taskFailure;
    private bool _taskListVisible;
    private bool _succeeding;
    private bool _waking;
    private bool _minimized;
    private bool _inactive;
    private TimeSpan _lastActivity;
    private long _reactions;
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

        _speakingRelease = CreateTimer(SpeakingRelease, OnSpeakingReleaseTick);
        _listeningRelease = CreateTimer(ListeningRelease, OnListeningReleaseTick);
        _successRelease = CreateTimer(SuccessHold, OnSuccessReleaseTick);
        _wakeRelease = CreateTimer(WakeHold, OnWakeReleaseTick);
        _sleepTimer = CreateTimer(SleepAfter, OnSleepTimerTick);

        _chat.PropertyChanged += OnChatPropertyChanged;
        _chat.Messages.CollectionChanged += OnMessagesChanged;
        _taskStore.TaskChanged += OnTaskChanged;
        _playback.PlaybackChanged += OnPlaybackChanged;
        _localization.LanguageChanged += OnLanguageChanged;

        ApprovalPromptWatcher.EnsureRegistered();
        ApprovalPromptWatcher.Changed += OnApprovalPromptChanged;

        _lastActivity = _clock.Elapsed;
        _sleepTimer.Start();

        _ = LoadBlockedTasksAsync();
        Resolve();
    }

    /// <summary>The single state the Living Core shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description))]
    private LivingCoreState _state = LivingCoreState.Idle;

    /// <summary>The latest reaction for the core: a new message, a panel opening.</summary>
    [ObservableProperty]
    private LivingCoreReaction? _reaction;

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

    /// <summary>
    /// The user touched HAMMOR (a key, a click, the wheel, the pointer over the
    /// window). It keeps the core awake, and wakes it from inactivity.
    /// </summary>
    public void ReportUserActivity()
    {
        if (_disposed)
        {
            return;
        }

        _lastActivity = _clock.Elapsed;
        if (!_sleepTimer.IsEnabled)
        {
            _sleepTimer.Interval = SleepAfter;
            _sleepTimer.Start();
        }

        if (_inactive)
        {
            _inactive = false;
            Resolve();
        }
    }

    /// <summary>
    /// The shell reports the window being minimised or coming back. Minimised
    /// is Sleep; coming back always plays Wake ("the window returns").
    /// </summary>
    public void SetWindowMinimized(bool minimized)
    {
        if (_disposed || minimized == _minimized)
        {
            return;
        }

        _minimized = minimized;
        if (!minimized)
        {
            ReportUserActivity();
            StartWaking();
        }

        Resolve();
    }

    /// <summary>
    /// The shell opened the navigation pane, on the leading side. The core
    /// glances toward it and the aura leans its way, where the state allows.
    /// </summary>
    public void NotifyPanelOpened() => React(LivingCoreReactionKind.PanelOpened, -1.0);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var timer in new[] { _speakingRelease, _listeningRelease, _successRelease, _wakeRelease, _sleepTimer })
        {
            timer.Stop();
        }

        _speakingRelease.Tick -= OnSpeakingReleaseTick;
        _listeningRelease.Tick -= OnListeningReleaseTick;
        _successRelease.Tick -= OnSuccessReleaseTick;
        _wakeRelease.Tick -= OnWakeReleaseTick;
        _sleepTimer.Tick -= OnSleepTimerTick;
        _chat.PropertyChanged -= OnChatPropertyChanged;
        _chat.Messages.CollectionChanged -= OnMessagesChanged;
        _taskStore.TaskChanged -= OnTaskChanged;
        _playback.PlaybackChanged -= OnPlaybackChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        ApprovalPromptWatcher.Changed -= OnApprovalPromptChanged;
    }

    internal static string DescriptionKey(LivingCoreState state) => state switch
    {
        LivingCoreState.Listening => "LivingCore.Listening",
        LivingCoreState.Thinking => "LivingCore.Thinking",
        LivingCoreState.Speaking => "LivingCore.Speaking",
        LivingCoreState.Blocked => "LivingCore.Blocked",
        LivingCoreState.Error => "LivingCore.Error",
        LivingCoreState.Success => "LivingCore.Success",
        LivingCoreState.Warning => "LivingCore.Warning",
        LivingCoreState.Sleep => "LivingCore.Sleep",
        LivingCoreState.Wake => "LivingCore.Wake",
        _ => "LivingCore.Idle",
    };

    private DispatcherTimer CreateTimer(TimeSpan interval, EventHandler tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = interval };
        timer.Tick += tick;
        return timer;
    }

    private void Resolve()
    {
        if (_disposed)
        {
            return;
        }

        var next = LivingCoreStateResolver.Resolve(new LivingCoreSignals(
            IsSpeaking: _speaking,
            IsListening: _listening,
            IsThinking: _chat.IsBusy && _awaitingReply,
            IsBlocked: _blockedTasks.Count > 0,
            HasError: _chatError || _taskFailure,
            IsConfirming: _confirming,
            IsSucceeding: _succeeding,
            IsResting: _minimized || _inactive,
            IsWaking: _waking));

        // Out of Sleep, always through Wake.
        if (LivingCoreStateResolver.NeedsWake(State, next))
        {
            StartWaking();
            next = LivingCoreState.Wake;
        }

        State = next;
    }

    private void React(LivingCoreReactionKind kind, double direction)
    {
        if (_disposed)
        {
            return;
        }

        Reaction = new LivingCoreReaction(kind, direction, ++_reactions);
    }

    // ---- Wake and Sleep ----

    private void StartWaking()
    {
        _waking = true;
        _wakeRelease.Stop();
        _wakeRelease.Start();
    }

    private void OnWakeReleaseTick(object? sender, EventArgs e)
    {
        _wakeRelease.Stop();
        _waking = false;
        Resolve();
    }

    private void OnSleepTimerTick(object? sender, EventArgs e)
    {
        _sleepTimer.Stop();

        var quietFor = _clock.Elapsed - _lastActivity;
        if (quietFor >= SleepAfter)
        {
            // Stays stopped until the user comes back.
            _inactive = true;
            Resolve();
            return;
        }

        // Activity moved the deadline; wait for the rest of it.
        var remaining = SleepAfter - quietFor;
        _sleepTimer.Interval = remaining < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining;
        _sleepTimer.Start();
    }

    // ---- Success: a task completed, or a blocked one was approved ----

    private void PulseSuccess()
    {
        _succeeding = true;
        _successRelease.Stop();
        _successRelease.Start();
    }

    private void OnSuccessReleaseTick(object? sender, EventArgs e)
    {
        _successRelease.Stop();
        _succeeding = false;
        Resolve();
    }

    // ---- Warning: an approval prompt is open ----

    private void OnApprovalPromptChanged(object? sender, EventArgs e) => OnUiThread(() =>
    {
        _confirming = ApprovalPromptWatcher.IsOpen;
        Resolve();
    });

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

    // ---- Listening: attention while the user types ----

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

    // ---- Chat: busy, sends, replies, failures ----

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatViewModel.IsBusy):
                if (_chat.IsBusy)
                {
                    _awaitingReply = true;
                    EndListening();
                }
                else
                {
                    _awaitingReply = false;
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
        var replied = false;
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
                        _awaitingReply = false;
                    }
                    else if (message.IsUser)
                    {
                        // Sending again is acting on an earlier failure.
                        _chatError = false;
                        EndListening();
                    }
                    else
                    {
                        // The reply is here: thinking is over, even while the
                        // turn finishes (speaking it, for example).
                        _awaitingReply = false;
                        replied = true;
                    }
                }

                break;

            case NotifyCollectionChangedAction.Reset:
                // The conversation was cleared.
                _chatError = false;
                break;
        }

        Resolve();

        if (replied)
        {
            // Toward the transcript: the trailing side of the conversation header.
            React(LivingCoreReactionKind.NewMessage, 1.0);
        }
    }

    // ---- Tasks: Blocked, approvals, completions and in-session failures ----

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

        if (ApplyTaskChange(task))
        {
            PulseSuccess();
        }

        Resolve();
    }

    /// <summary>Applies one task change; true when it is a success to show.</summary>
    private bool ApplyTaskChange(HammorTask task)
    {
        var wasBlocked = _blockedTasks.Contains(task.Id);
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

        // "When a task completes, or you approve" it: a blocked task resumed
        // with a new grant goes back to Pending.
        return task.State == TaskState.Completed
            || (wasBlocked && task.State is TaskState.Pending or TaskState.Running);
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
