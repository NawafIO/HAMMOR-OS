using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.App.Services;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Localization;
using HAMMOR.Core.Security;
using HAMMOR.Core.Status;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Voice;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.ViewModels;

/// <summary>
/// Edits a working copy of the configuration and persists it on save.
/// </summary>
/// <remarks>
/// Secrets are handled separately from the rest of the settings: they are
/// written straight to <see cref="ISecretStore"/> by their own commands and
/// never bound into the configuration object, so a key cannot reach the JSON
/// file even accidentally. The UI only ever displays whether a key exists.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigurationStore _configurationStore;
    private readonly ISecretStore _secretStore;
    private readonly ILocalizationService _localization;
    private readonly IThemeService _themeService;
    private readonly ISystemStatusService _statusService;
    private readonly IAudioDeviceProvider _audioDevices;
    private readonly IVoiceOrchestrator _voice;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        IConfigurationStore configurationStore,
        ISecretStore secretStore,
        ILocalizationService localization,
        IThemeService themeService,
        ISystemStatusService statusService,
        IAudioDeviceProvider audioDevices,
        IVoiceOrchestrator voice,
        ILogger<SettingsViewModel> logger)
    {
        _configurationStore = configurationStore
                              ?? throw new ArgumentNullException(nameof(configurationStore));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _audioDevices = audioDevices ?? throw new ArgumentNullException(nameof(audioDevices));
        _voice = voice ?? throw new ArgumentNullException(nameof(voice));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _draft = configurationStore.Current.Clone();
        ConfigurationFilePath = configurationStore.ConfigurationFilePath;

        _localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Working copy. Nothing is persisted until Save runs.</summary>
    [ObservableProperty]
    private HammorConfiguration _draft;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _anthropicKeyInput = string.Empty;

    [ObservableProperty]
    private string _elevenLabsKeyInput = string.Empty;

    [ObservableProperty]
    private bool _anthropicKeyStored;

    [ObservableProperty]
    private bool _elevenLabsKeyStored;

    public string ConfigurationFilePath { get; }

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();

    public IReadOnlyList<string> AvailableEfforts { get; } =
        ["low", "medium", "high", "xhigh", "max"];

    /// <summary>
    /// Auto-approve options stop at Execute on purpose. Destructive is not
    /// offered because destructive tools always require confirmation —
    /// listing it would imply the floor can be configured away.
    /// </summary>
    public IReadOnlyList<ToolPermission> AutoApproveOptions { get; } =
        [ToolPermission.Read, ToolPermission.Write, ToolPermission.Execute];

    public IReadOnlyList<AudioDevice> OutputDevices { get; private set; } = [];

    public IReadOnlyList<AudioDevice> InputDevices { get; private set; } = [];

    /// <summary>
    /// Selected UI language. Setting it applies and persists immediately
    /// rather than waiting for Save, so the effect is visible as soon as the
    /// user picks it.
    /// </summary>
    public string SelectedLanguage
    {
        get => _localization.CurrentLanguage;
        set
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, _localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = ApplyLanguageAsync(value);
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Draft = _configurationStore.Current.Clone();

        OutputDevices = _audioDevices.GetOutputDevices();
        InputDevices = _audioDevices.GetInputDevices();
        OnPropertyChanged(nameof(OutputDevices));
        OnPropertyChanged(nameof(InputDevices));

        AnthropicKeyStored = await _secretStore
            .ExistsAsync(SecretNames.AnthropicApiKey).ConfigureAwait(true);

        ElevenLabsKeyStored = await _secretStore
            .ExistsAsync(SecretNames.ElevenLabsApiKey).ConfigureAwait(true);

        OnPropertyChanged(nameof(SelectedLanguage));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            // Keep the language from the live service: it is applied
            // immediately on selection, so the draft could be stale here.
            Draft.General.Language = _localization.CurrentLanguage;

            await _configurationStore.SaveAsync(Draft).ConfigureAwait(true);

            _themeService.Apply(Draft.General.Theme);

            await _statusService.RefreshAsync().ConfigureAwait(true);

            StatusMessage = Localization.LocalizationSource.Instance["Settings.Saved"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save settings.");
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveAnthropicKeyAsync()
    {
        var key = AnthropicKeyInput.Trim();
        if (key.Length == 0)
        {
            return;
        }

        await _secretStore.SetAsync(SecretNames.AnthropicApiKey, key).ConfigureAwait(true);

        // Clear the input immediately so the key does not sit in a bound
        // property any longer than needed.
        AnthropicKeyInput = string.Empty;
        AnthropicKeyStored = true;

        await _statusService.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearAnthropicKeyAsync()
    {
        await _secretStore.DeleteAsync(SecretNames.AnthropicApiKey).ConfigureAwait(true);
        AnthropicKeyStored = false;
        await _statusService.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveElevenLabsKeyAsync()
    {
        var key = ElevenLabsKeyInput.Trim();
        if (key.Length == 0)
        {
            return;
        }

        await _secretStore.SetAsync(SecretNames.ElevenLabsApiKey, key).ConfigureAwait(true);

        ElevenLabsKeyInput = string.Empty;
        ElevenLabsKeyStored = true;

        await _statusService.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearElevenLabsKeyAsync()
    {
        await _secretStore.DeleteAsync(SecretNames.ElevenLabsApiKey).ConfigureAwait(true);
        ElevenLabsKeyStored = false;
        await _statusService.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TestConnectionsAsync()
    {
        var status = await _statusService.RefreshAsync().ConfigureAwait(true);

        StatusMessage =
            $"{Localization.LocalizationSource.Instance["Status.Claude"]}: {status.Claude.Detail}"
            + Environment.NewLine
            + $"{Localization.LocalizationSource.Instance["Status.ElevenLabs"]}: "
            + status.TextToSpeech.Detail;
    }

    /// <summary>
    /// Speaks a short phrase through the configured voice so the user can
    /// verify the voice id and device without starting a conversation.
    /// </summary>
    [RelayCommand]
    private async Task TestSpeechAsync()
    {
        // Save first: the test must use the voice id currently on screen, not
        // the last-persisted one.
        await _configurationStore.SaveAsync(Draft).ConfigureAwait(true);

        var phrase = Localization.LocalizationSource.Instance["Settings.Voice.TestPhrase"];

        var spoke = await _voice
            .SpeakAsync(phrase, _localization.CurrentLanguage)
            .ConfigureAwait(true);

        StatusMessage = spoke
            ? phrase
            : Localization.LocalizationSource.Instance["Common.NotConfigured"];
    }

    private async Task ApplyLanguageAsync(string languageCode)
    {
        try
        {
            await _localization.SetLanguageAsync(languageCode).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not switch language to {Language}.", languageCode);
            StatusMessage = ex.Message;

            // The selector already shows the language that failed to apply;
            // put it back to the one in use.
            OnPropertyChanged(nameof(SelectedLanguage));
        }
    }

    /// <summary>
    /// Keeps the selector on the language actually in use, wherever the
    /// switch came from: this page, the shell's quick toggle or first run.
    /// </summary>
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        // SetLanguageAsync persisted the language; refresh the draft so the
        // next Save does not write a stale value back.
        Draft.General.Language = _localization.CurrentLanguage;
        OnPropertyChanged(nameof(SelectedLanguage));
    }
}
