using System.IO;
// WPF implicitly imports System.Windows.Shapes, whose Path type collides with
// System.IO.Path. The alias keeps file-system path handling unambiguous.
using IoPath = System.IO.Path;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Localization;
using HAMMOR.Core.Security;
using HAMMOR.Core.Status;
using HAMMOR.Core.Storage;
using HAMMOR.Core.Voice;
using HAMMOR.Infrastructure.Ai.ClaudeCode;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.ViewModels;

/// <summary>
/// Drives the first-run wizard: language, Claude, voice, memory location,
/// then a completion step.
/// </summary>
/// <remarks>
/// Every step after the language choice is skippable. Blocking setup on an API
/// key the user does not have yet would leave them unable to reach Settings,
/// so the wizard records what was configured and lets the rest be filled in
/// later.
/// </remarks>
public sealed partial class FirstRunViewModel : ObservableObject
{
    /// <summary>Total steps, including the final confirmation.</summary>
    public const int StepCount = 5;

    private readonly IConfigurationStore _configurationStore;
    private readonly ISecretStore _secretStore;
    private readonly ILocalizationService _localization;
    private readonly ISystemStatusService _statusService;
    private readonly IAudioDeviceProvider _audioDevices;
    private readonly ILogger<FirstRunViewModel> _logger;

    public FirstRunViewModel(
        IConfigurationStore configurationStore,
        ISecretStore secretStore,
        ILocalizationService localization,
        ISystemStatusService statusService,
        IAudioDeviceProvider audioDevices,
        HammorPaths paths,
        ILogger<FirstRunViewModel> logger)
    {
        _configurationStore = configurationStore
                              ?? throw new ArgumentNullException(nameof(configurationStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _draft = configurationStore.Current.Clone();

        if (string.IsNullOrWhiteSpace(_draft.Memory.RootPath))
        {
            _draft.Memory.RootPath = paths.DataRoot;
        }

        OutputDevices = _audioDevices.GetOutputDevices();
        InputDevices = _audioDevices.GetInputDevices();

        ClaudeCodeDetected = DetectClaudeCode();
    }

    [ObservableProperty]
    private HammorConfiguration _draft;

    [ObservableProperty]
    private int _stepIndex;

    [ObservableProperty]
    private string _anthropicKeyInput = string.Empty;

    [ObservableProperty]
    private string _elevenLabsKeyInput = string.Empty;

    [ObservableProperty]
    private bool _isFinished;

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    public IReadOnlyList<AudioDevice> OutputDevices { get; }

    public IReadOnlyList<AudioDevice> InputDevices { get; }

    /// <summary>
    /// Whether the Claude Code CLI is on PATH. Reported for information only —
    /// HAMMOR talks to the Claude API directly and does not require it.
    /// </summary>
    public bool ClaudeCodeDetected { get; }

    public bool HasMicrophone => InputDevices.Count > 0;

    public bool HasSpeaker => OutputDevices.Count > 0;

    public bool IsLanguageStep => StepIndex == 0;

    public bool IsAiStep => StepIndex == 1;

    public bool IsVoiceStep => StepIndex == 2;

    public bool IsMemoryStep => StepIndex == 3;

    public bool IsFinalStep => StepIndex == StepCount - 1;

    public bool CanGoBack => StepIndex > 0;

    /// <summary>Progress label, e.g. "Step 2 / 5".</summary>
    public string StepLabel =>
        $"{Localization.LocalizationSource.Instance["Setup.Step"]} {StepIndex + 1} / {StepCount}";

    public string SelectedLanguage
    {
        get => _localization.CurrentLanguage;
        set
        {
            if (!string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, _localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                // Applied immediately so the rest of the wizard is already in
                // the chosen language.
                _ = ApplyLanguageAsync(value);
            }
        }
    }

    /// <summary>Raised when the wizard is done and the shell may open.</summary>
    public event EventHandler<bool>? CompletionRequested;

    [RelayCommand]
    private void Next()
    {
        if (StepIndex < StepCount - 1)
        {
            StepIndex++;
            NotifyStepChanged();
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (StepIndex > 0)
        {
            StepIndex--;
            NotifyStepChanged();
        }
    }

    /// <summary>Stores any keys entered, persists settings, closes the wizard.</summary>
    [RelayCommand]
    private async Task FinishAsync()
    {
        try
        {
            // With an API key, Claude comes through the API. Without one, it
            // comes through the user's own Claude Code and Claude account: an
            // API key is optional, never required.
            var hasApiKey = AnthropicKeyInput.Trim().Length > 0;
            Draft.Ai.PrimaryProvider = hasApiKey ? "claude" : ClaudeCodeAiProvider.Id;

            if (hasApiKey)
            {
                await _secretStore
                    .SetAsync(SecretNames.AnthropicApiKey, AnthropicKeyInput.Trim())
                    .ConfigureAwait(true);

                AnthropicKeyInput = string.Empty;
            }

            if (ElevenLabsKeyInput.Trim().Length > 0)
            {
                await _secretStore
                    .SetAsync(SecretNames.ElevenLabsApiKey, ElevenLabsKeyInput.Trim())
                    .ConfigureAwait(true);

                ElevenLabsKeyInput = string.Empty;
            }

            Draft.General.Language = _localization.CurrentLanguage;
            Draft.SetupCompleted = true;

            await _configurationStore.SaveAsync(Draft).ConfigureAwait(true);
            await _statusService.RefreshAsync().ConfigureAwait(true);

            IsFinished = true;
            CompletionRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            // Setup failing to persist is serious: the user would be sent back
            // through the wizard on next launch with no explanation.
            _logger.LogError(ex, "Could not complete first-run setup.");
            throw;
        }
    }

    private async Task ApplyLanguageAsync(string languageCode)
    {
        await _localization.SetLanguageAsync(languageCode).ConfigureAwait(true);
        Draft.General.Language = _localization.CurrentLanguage;
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    private void NotifyStepChanged()
    {
        OnPropertyChanged(nameof(IsLanguageStep));
        OnPropertyChanged(nameof(IsAiStep));
        OnPropertyChanged(nameof(IsVoiceStep));
        OnPropertyChanged(nameof(IsMemoryStep));
        OnPropertyChanged(nameof(IsFinalStep));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(StepLabel));
    }

    /// <summary>
    /// Looks for the Claude Code CLI on PATH. Informational only: HAMMOR is
    /// the orchestration layer and uses the Claude API directly, so a missing
    /// CLI changes nothing about setup.
    /// </summary>
    private static bool DetectClaudeCode()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        var executables = new[] { "claude.exe", "claude.cmd", "claude" };

        foreach (var directory in pathVariable.Split(IoPath.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                if (executables.Any(exe => File.Exists(IoPath.Combine(directory, exe))))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry (invalid characters) is not worth
                // failing detection over — skip it and keep looking.
            }
        }

        return false;
    }
}
