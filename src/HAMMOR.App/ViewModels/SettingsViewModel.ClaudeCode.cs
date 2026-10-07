using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.App.Localization;
using HAMMOR.Core.Configuration;
using HAMMOR.Infrastructure.Ai.ClaudeCode;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.ViewModels;

/// <summary>A provider the user can choose in Settings.</summary>
/// <param name="Id">Matched against <see cref="AiSettings.PrimaryProvider"/>.</param>
/// <param name="LabelKey">Localisation key of its label.</param>
public sealed record ProviderOption(string Id, string LabelKey)
{
    public string Label => LocalizationSource.Instance[LabelKey];
}

/// <summary>How serious a Claude Code state is, for its status dot.</summary>
public enum ClaudeCodeIndicator
{
    Neutral = 0,
    Good = 1,
    Attention = 2,
    Problem = 3,
}

/// <summary>
/// Settings → AI provider → Claude: the provider choice and the Claude Code
/// account panel.
/// </summary>
/// <remarks>
/// HAMMOR only shows what Claude Code reports about itself (installed,
/// signed in, by which method, limit reached). Sign-in opens Claude Code's
/// own window; no credential passes through HAMMOR.
/// </remarks>
public sealed partial class SettingsViewModel
{
    private const string ApiProviderId = "claude";

    private IClaudeCodeAccount? _claudeCode;

    /// <summary>Claude Code first: the account path needs no API key.</summary>
    public IReadOnlyList<ProviderOption> ProviderOptions { get; } =
    [
        new(ClaudeCodeAiProvider.Id, "Settings.Ai.Provider.ClaudeCode"),
        new(ApiProviderId, "Settings.Ai.Provider.ApiKey"),
    ];

    /// <summary>The chosen provider; saved with the other settings.</summary>
    public string SelectedProviderId
    {
        get => Draft.Ai.PrimaryProvider;
        set
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, Draft.Ai.PrimaryProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Draft.Ai.PrimaryProvider = value;
            NotifyProviderChoice();
        }
    }

    public bool IsClaudeCodeSelected =>
        string.Equals(Draft.Ai.PrimaryProvider, ClaudeCodeAiProvider.Id, StringComparison.OrdinalIgnoreCase);

    public bool IsApiKeySelected => !IsClaudeCodeSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClaudeCodeStateText))]
    [NotifyPropertyChangedFor(nameof(ClaudeCodeDetailText))]
    [NotifyPropertyChangedFor(nameof(ClaudeCodeLevel))]
    [NotifyPropertyChangedFor(nameof(CanSignInToClaudeCode))]
    [NotifyPropertyChangedFor(nameof(CanReconnectClaudeCode))]
    [NotifyPropertyChangedFor(nameof(CanSignOutOfClaudeCode))]
    [NotifyPropertyChangedFor(nameof(ShowClaudeCodeInstallHint))]
    [NotifyPropertyChangedFor(nameof(ShowClaudeCodeLauncherHint))]
    [NotifyPropertyChangedFor(nameof(ShowClaudeCodeUpdateHint))]
    private ClaudeCodeStatus _claudeCodeStatus = ClaudeCodeStatus.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClaudeCodeStateText))]
    [NotifyPropertyChangedFor(nameof(CanSignInToClaudeCode))]
    [NotifyPropertyChangedFor(nameof(CanReconnectClaudeCode))]
    [NotifyPropertyChangedFor(nameof(CanSignOutOfClaudeCode))]
    private bool _isClaudeCodeBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignInToClaudeCode))]
    [NotifyPropertyChangedFor(nameof(CanReconnectClaudeCode))]
    [NotifyPropertyChangedFor(nameof(CanSignOutOfClaudeCode))]
    private bool _isWaitingForClaudeCodeSignIn;

    public string ClaudeCodeStateText => LocalizationSource.Instance[IsClaudeCodeBusy && ClaudeCodeStatus.CheckedAt == DateTimeOffset.MinValue
        ? "Settings.ClaudeCode.State.Checking"
        : ClaudeCodeStatus.State switch
        {
            ClaudeCodeState.UnsupportedInstall => "Settings.ClaudeCode.State.UnsupportedInstall",
            ClaudeCodeState.NotSignedIn => "Settings.ClaudeCode.State.NotSignedIn",
            ClaudeCodeState.SignedIn => "Settings.ClaudeCode.State.SignedIn",
            ClaudeCodeState.UsageLimited => "Settings.ClaudeCode.State.UsageLimited",
            ClaudeCodeState.NeedsUpdate => "Settings.ClaudeCode.State.NeedsUpdate",
            ClaudeCodeState.Error => "Settings.ClaudeCode.State.Error",
            _ => "Settings.ClaudeCode.State.NotInstalled",
        }];

    /// <summary>Version, sign-in method, and Claude Code's own message, where there is one.</summary>
    public string ClaudeCodeDetailText
    {
        get
        {
            var status = ClaudeCodeStatus;
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(status.Version))
            {
                parts.Add(string.Format(
                    CultureInfo.CurrentCulture, LocalizationSource.Instance["Settings.ClaudeCode.Version"], status.Version));
            }

            if (status.State is ClaudeCodeState.SignedIn or ClaudeCodeState.UsageLimited)
            {
                parts.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationSource.Instance["Settings.ClaudeCode.Method"],
                    LocalizationSource.Instance[MethodKey(status.AuthMethod)]));
            }

            var detail = string.Join(" · ", parts);
            return string.IsNullOrEmpty(status.Detail) ? detail : detail.Length == 0 ? status.Detail : detail + "\n" + status.Detail;
        }
    }

    public ClaudeCodeIndicator ClaudeCodeLevel => ClaudeCodeStatus.State switch
    {
        ClaudeCodeState.SignedIn => ClaudeCodeIndicator.Good,
        ClaudeCodeState.UsageLimited or ClaudeCodeState.NeedsUpdate => ClaudeCodeIndicator.Attention,
        ClaudeCodeState.Error => ClaudeCodeIndicator.Problem,
        _ => ClaudeCodeIndicator.Neutral,
    };

    public bool CanSignInToClaudeCode => !IsClaudeCodeBusy && !IsWaitingForClaudeCodeSignIn
        && ClaudeCodeStatus.State == ClaudeCodeState.NotSignedIn;

    public bool CanReconnectClaudeCode => !IsClaudeCodeBusy && !IsWaitingForClaudeCodeSignIn
        && ClaudeCodeStatus.State is ClaudeCodeState.SignedIn or ClaudeCodeState.UsageLimited or ClaudeCodeState.Error;

    public bool CanSignOutOfClaudeCode => !IsClaudeCodeBusy && !IsWaitingForClaudeCodeSignIn
        && ClaudeCodeStatus.State is ClaudeCodeState.SignedIn or ClaudeCodeState.UsageLimited;

    public bool ShowClaudeCodeInstallHint => ClaudeCodeStatus.State == ClaudeCodeState.NotInstalled;

    public bool ShowClaudeCodeLauncherHint => ClaudeCodeStatus.State == ClaudeCodeState.UnsupportedInstall;

    public bool ShowClaudeCodeUpdateHint => ClaudeCodeStatus.State == ClaudeCodeState.NeedsUpdate;

    private void InitializeClaudeCode(IClaudeCodeAccount claudeCode)
    {
        _claudeCode = claudeCode ?? throw new ArgumentNullException(nameof(claudeCode));
        ClaudeCodeStatus = claudeCode.Current;
        claudeCode.StatusChanged += OnClaudeCodeStatusChanged;
    }

    partial void OnDraftChanged(HammorConfiguration value) => NotifyProviderChoice();

    private void NotifyProviderChoice()
    {
        OnPropertyChanged(nameof(SelectedProviderId));
        OnPropertyChanged(nameof(IsClaudeCodeSelected));
        OnPropertyChanged(nameof(IsApiKeySelected));
    }

    /// <summary>Checks Claude Code when Settings opens, re-using a recent check.</summary>
    private async Task LoadClaudeCodeAsync()
    {
        await RunClaudeCodeAsync(account => account.GetStatusAsync(TimeSpan.FromSeconds(30))).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task RefreshClaudeCodeAsync() => RunClaudeCodeAsync(account => account.RefreshAsync());

    [RelayCommand]
    private Task SignInToClaudeCodeAsync() => SignInAsync();

    /// <summary>Reconnecting is signing in again through Claude Code's own flow.</summary>
    [RelayCommand]
    private Task ReconnectClaudeCodeAsync() => SignInAsync();

    [RelayCommand]
    private Task SignOutOfClaudeCodeAsync() => RunClaudeCodeAsync(account => account.SignOutAsync());

    /// <summary>
    /// Waits for Claude Code's own sign-in window without blocking the panel:
    /// Refresh stays available while the window is open.
    /// </summary>
    private async Task SignInAsync()
    {
        if (_claudeCode is null || IsWaitingForClaudeCodeSignIn)
        {
            return;
        }

        IsWaitingForClaudeCodeSignIn = true;
        try
        {
            ClaudeCodeStatus = await _claudeCode.SignInAsync().ConfigureAwait(true);
            await _statusService.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude Code sign-in could not be completed.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsWaitingForClaudeCodeSignIn = false;
        }
    }

    private async Task RunClaudeCodeAsync(Func<IClaudeCodeAccount, Task<ClaudeCodeStatus>> action)
    {
        if (_claudeCode is null || IsClaudeCodeBusy)
        {
            return;
        }

        IsClaudeCodeBusy = true;
        try
        {
            ClaudeCodeStatus = await action(_claudeCode).ConfigureAwait(true);
            await _statusService.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude Code status could not be updated.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsClaudeCodeBusy = false;
        }
    }

    private void OnClaudeCodeStatusChanged(object? sender, ClaudeCodeStatus status)
    {
        // Raised from wherever Claude Code finished; the UI updates on its thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ClaudeCodeStatus = status;
        }
        else
        {
            _ = dispatcher.InvokeAsync(() => ClaudeCodeStatus = status);
        }
    }

    private static string MethodKey(string? authMethod) => authMethod switch
    {
        "claude.ai" => "Settings.ClaudeCode.Method.Subscription",
        "oauth_token" => "Settings.ClaudeCode.Method.Token",
        "api_key" or "api_key_helper" => "Settings.ClaudeCode.Method.ApiKey",
        "third_party" => "Settings.ClaudeCode.Method.ThirdParty",
        _ => "Settings.ClaudeCode.Method.Unknown",
    };
}
