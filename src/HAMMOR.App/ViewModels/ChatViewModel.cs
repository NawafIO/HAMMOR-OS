using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.App.Localization;
using HAMMOR.Core.Agent;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Localization;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Status;
using HAMMOR.Core.Voice;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.ViewModels;

/// <summary>A message rendered in the chat transcript.</summary>
public sealed partial class ChatMessageViewModel(AiRole role, string text, bool isError = false)
    : ObservableObject
{
    public AiRole Role { get; } = role;

    public string Text { get; } = text;

    public bool IsError { get; } = isError;

    public bool IsUser => Role == AiRole.User;

    public bool IsAssistant => Role == AiRole.Assistant;

    /// <summary>When the message appeared on screen.</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;

    /// <summary>The time, in the current language's short format.</summary>
    public string TimeLabel => Timestamp.ToString("t", CultureInfo.CurrentCulture);

    /// <summary>Localised author label.</summary>
    public string AuthorLabel =>
        IsUser ? LocalizationSource.Instance["Chat.You"] : LocalizationSource.Instance["App.Title"];
}

/// <summary>Drives the chat page.</summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IAgentPipeline _pipeline;
    private readonly IVoiceOrchestrator _voice;
    private readonly ISystemStatusService _statusService;
    private readonly IConfigurationStore _configurationStore;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ChatViewModel> _logger;

    private CancellationTokenSource? _turnCancellation;

    public ChatViewModel(
        IAgentPipeline pipeline,
        IVoiceOrchestrator voice,
        ISystemStatusService statusService,
        IConfigurationStore configurationStore,
        ILocalizationService localization,
        ILogger<ChatViewModel> logger)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _voice = voice ?? throw new ArgumentNullException(nameof(voice));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _configurationStore = configurationStore
                              ?? throw new ArgumentNullException(nameof(configurationStore));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelTurnCommand))]
    private bool _isBusy;

    /// <summary>
    /// The project the next turns belong to, or null. The pipeline then adds
    /// the project's standing context to the prompt and saves the exchange
    /// under the project, as Core already supports; nothing else changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveProject))]
    [NotifyPropertyChangedFor(nameof(ActiveProjectName))]
    private HammorProject? _activeProject;

    public bool HasActiveProject => ActiveProject is not null;

    public string ActiveProjectName => ActiveProject?.Name ?? string.Empty;

    /// <summary>Current pipeline stage, shown while a turn is running.</summary>
    [ObservableProperty]
    private string _progressDetail = string.Empty;

    public bool HasMessages => Messages.Count > 0;

    /// <summary>
    /// Voice input is permanently disabled in Phase 1: no STT provider is
    /// implemented. The UI shows the reason rather than a dead button.
    /// </summary>
    public bool IsVoiceInputAvailable => false;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Input = string.Empty;
        Messages.Add(new ChatMessageViewModel(AiRole.User, text));
        OnPropertyChanged(nameof(HasMessages));

        IsBusy = true;
        _statusService.SetBusy(true);
        SendCommand.NotifyCanExecuteChanged();

        _turnCancellation?.Dispose();
        _turnCancellation = new CancellationTokenSource();

        try
        {
            // History excludes the message just added and any error bubbles:
            // an error is HAMMOR's own diagnostic, not part of the dialogue.
            var history = Messages
                .Where(m => !m.IsError)
                .SkipLast(1)
                .Select(m => new AiMessage(m.Role, m.Text))
                .ToList();

            var progress = new Progress<AgentProgress>(p => ProgressDetail = p.Detail);

            var result = await _pipeline.RunAsync(
                new AgentTurnRequest
                {
                    Input = text,
                    History = history,
                    ProjectId = ActiveProject?.Id,
                    Language = _localization.CurrentLanguage,
                },
                progress,
                _turnCancellation.Token).ConfigureAwait(true);

            if (result.Succeeded)
            {
                Messages.Add(new ChatMessageViewModel(AiRole.Assistant, result.ReplyText));

                if (_configurationStore.Current.Voice.SpeakResponsesAutomatically)
                {
                    await SpeakAsync(result.ReplyText).ConfigureAwait(true);
                }
            }
            else
            {
                // Failures are shown in the transcript, attributed to the
                // stage that failed, so the user can tell a configuration
                // problem from a model refusal.
                Messages.Add(new ChatMessageViewModel(
                    AiRole.Assistant,
                    $"[{result.ReachedStage}] {result.Error}",
                    isError: true));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Chat turn cancelled by the user.");
        }
        finally
        {
            IsBusy = false;
            ProgressDetail = string.Empty;
            _statusService.SetBusy(false);
            SendCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasMessages));
        }
    }

    private bool CanSend() => !IsBusy;

    /// <summary>Stops the turn in progress. The question stays; no reply is added.</summary>
    [RelayCommand(CanExecute = nameof(CanCancelTurn))]
    private void CancelTurn() => _turnCancellation?.Cancel();

    private bool CanCancelTurn() => IsBusy;

    /// <summary>Back to unscoped conversation.</summary>
    [RelayCommand]
    private void ClearProject() => ActiveProject = null;

    /// <summary>Speaks the most recent assistant reply.</summary>
    [RelayCommand]
    private async Task SpeakLastAsync()
    {
        var last = Messages.LastOrDefault(m => m.IsAssistant && !m.IsError);

        if (last is not null)
        {
            await SpeakAsync(last.Text).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void StopSpeaking() => _voice.StopSpeaking();

    [RelayCommand]
    private void Clear()
    {
        Messages.Clear();
        OnPropertyChanged(nameof(HasMessages));
    }

    private async Task SpeakAsync(string text)
    {
        // SpeakAsync returns false when TTS is unconfigured; that is a normal
        // state, not an error worth interrupting the conversation for.
        var spoke = await _voice
            .SpeakAsync(text, _localization.CurrentLanguage)
            .ConfigureAwait(true);

        if (!spoke)
        {
            _logger.LogInformation("Reply not spoken: text-to-speech is unavailable.");
        }
    }
}
