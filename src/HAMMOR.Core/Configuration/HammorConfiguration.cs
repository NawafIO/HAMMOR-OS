namespace HAMMOR.Core.Configuration;

/// <summary>
/// Root of HAMMOR's persisted settings. Mutable so the settings UI can edit a
/// working copy, then hand it to <see cref="IConfigurationStore.SaveAsync"/>.
/// </summary>
/// <remarks>
/// Contains no secrets. API keys live in <see cref="Security.ISecretStore"/>
/// (Windows DPAPI); this object only records which providers are selected and
/// how they are tuned, so it is safe to write as plain JSON.
/// </remarks>
public sealed class HammorConfiguration
{
    public GeneralSettings General { get; set; } = new();

    public AiSettings Ai { get; set; } = new();

    public VoiceSettings Voice { get; set; } = new();

    public MemorySettings Memory { get; set; } = new();

    public SecuritySettings Security { get; set; } = new();

    /// <summary>
    /// False until the first-run wizard completes. Drives whether the shell
    /// opens the wizard instead of the main window.
    /// </summary>
    public bool SetupCompleted { get; set; }

    /// <summary>Schema version, for forward-compatible migration.</summary>
    public int SchemaVersion { get; set; } = 1;

    public HammorConfiguration Clone() => new()
    {
        SetupCompleted = SetupCompleted,
        SchemaVersion = SchemaVersion,
        General = General.Clone(),
        Ai = Ai.Clone(),
        Voice = Voice.Clone(),
        Memory = Memory.Clone(),
        Security = Security.Clone(),
    };
}

public sealed class GeneralSettings
{
    /// <summary>BCP-47 language code for the UI. Persisted across launches.</summary>
    public string Language { get; set; } = "en";

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool LaunchOnStartup { get; set; }

    public bool StartMinimisedToTray { get; set; }

    public GeneralSettings Clone() => (GeneralSettings)MemberwiseClone();
}

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public sealed class AiSettings
{
    /// <summary>
    /// Id of the provider to route through, matched against
    /// <c>IAiProvider.ProviderId</c>. Claude is primary for v1.
    /// </summary>
    public string PrimaryProvider { get; set; } = "claude";

    /// <summary>
    /// Model identifier, kept in configuration so it can be changed without a
    /// rebuild — same rule as the ElevenLabs voice id.
    /// </summary>
    public string Model { get; set; } = "claude-opus-5";

    public int MaxTokens { get; set; } = 16000;

    /// <summary>
    /// Thinking effort passed through to the provider: low | medium | high |
    /// xhigh | max. Validated by the provider, not here.
    /// </summary>
    public string Effort { get; set; } = "high";

    /// <summary>Optional system prompt prefix applied to every turn.</summary>
    public string SystemPrompt { get; set; } =
        "You are HAMMOR, a Windows desktop assistant. Answer in the user's language.";

    public AiSettings Clone() => (AiSettings)MemberwiseClone();
}

public sealed class VoiceSettings
{
    /// <summary>Selected TTS provider id, e.g. <c>elevenlabs</c>.</summary>
    public string TtsProvider { get; set; } = "elevenlabs";

    /// <summary>
    /// Selected STT provider id. Defaults to the explicitly-unimplemented
    /// provider so nothing pretends speech recognition works.
    /// </summary>
    public string SttProvider { get; set; } = "none";

    public ElevenLabsSettings ElevenLabs { get; set; } = new();

    /// <summary>NAudio device id for capture, or null for the system default.</summary>
    public string? InputDeviceId { get; set; }

    /// <summary>NAudio device id for playback, or null for the system default.</summary>
    public string? OutputDeviceId { get; set; }

    /// <summary>Speak responses aloud automatically when TTS is configured.</summary>
    public bool SpeakResponsesAutomatically { get; set; }

    /// <summary>Playback volume, 0.0 to 1.0.</summary>
    public double OutputVolume { get; set; } = 1.0;

    public VoiceSettings Clone()
    {
        var copy = (VoiceSettings)MemberwiseClone();
        copy.ElevenLabs = ElevenLabs.Clone();
        return copy;
    }
}

public sealed class ElevenLabsSettings
{
    /// <summary>
    /// Configured ElevenLabs voice id. Seeded with the voice HAMMOR ships
    /// with, but it is data: the user can change it in Settings and nothing in
    /// the TTS pipeline hard-codes it.
    /// </summary>
    public string VoiceId { get; set; } = "G3YpdjT1OTh9cunaumJs";

    public string ModelId { get; set; } = "eleven_multilingual_v2";

    /// <summary>Output format requested from the API.</summary>
    public string OutputFormat { get; set; } = "mp3_44100_128";

    public double Stability { get; set; } = 0.5;

    public double SimilarityBoost { get; set; } = 0.75;

    public ElevenLabsSettings Clone() => (ElevenLabsSettings)MemberwiseClone();
}

public sealed class MemorySettings
{
    /// <summary>
    /// Root folder for the SQLite database and human-readable markdown notes.
    /// Empty means "use the per-user default under LocalApplicationData".
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Re-index markdown on startup. Indexing is incremental (size + mtime +
    /// content hash), so this is cheap when nothing changed.
    /// </summary>
    public bool IndexOnStartup { get; set; } = true;

    public MemorySettings Clone() => (MemorySettings)MemberwiseClone();
}

public sealed class SecuritySettings
{
    /// <summary>
    /// Highest permission level that may run without an explicit prompt.
    /// Defaults to Read: anything that writes, executes, or destroys asks.
    /// </summary>
    public Tools.ToolPermission AutoApproveUpTo { get; set; } = Tools.ToolPermission.Read;

    /// <summary>
    /// When true, destructive tools always prompt even if
    /// <see cref="AutoApproveUpTo"/> would otherwise cover them. This is a
    /// floor that cannot be configured away.
    /// </summary>
    public bool AlwaysConfirmDestructive { get; set; } = true;

    /// <summary>Retain audit rows for this many days. Zero keeps them forever.</summary>
    public int AuditRetentionDays { get; set; }

    /// <summary>
    /// Additional filesystem roots that filesystem tools are allowed to access.
    /// When null or empty, only <see cref="Storage.HammorPaths.DataRoot"/> (and
    /// <see cref="MemorySettings.RootPath"/> when set) are approved.
    /// No hard-coded machine paths — values come from configuration.
    /// </summary>
    public List<string> FilesystemAllowedRoots { get; set; } = new();

    public SecuritySettings Clone()
    {
        var copy = (SecuritySettings)MemberwiseClone();
        copy.FilesystemAllowedRoots = new List<string>(FilesystemAllowedRoots);
        return copy;
    }
}
