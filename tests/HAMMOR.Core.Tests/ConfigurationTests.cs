using System.Text.Json;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Storage;
using HAMMOR.Core.Tools;
using HAMMOR.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HAMMOR.Core.Tests;

/// <summary>
/// Configuration round-trip, plus the Phase 1 requirement that the ElevenLabs
/// voice id is configuration rather than a constant in the pipeline.
/// </summary>
public sealed class ConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "hammor-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task Load_returns_defaults_when_no_file_exists()
    {
        var store = CreateStore();

        var configuration = await store.LoadAsync();

        Assert.False(configuration.SetupCompleted);
        Assert.Equal("en", configuration.General.Language);
        Assert.Equal("claude", configuration.Ai.PrimaryProvider);
    }

    /// <summary>
    /// The shipped voice id must be reachable as data. If this value ever has
    /// to be changed in source, the "configurable voice id" requirement is
    /// broken.
    /// </summary>
    [Fact]
    public void Default_voice_id_is_the_configured_hammor_voice()
    {
        var configuration = new HammorConfiguration();

        Assert.Equal("G3YpdjT1OTh9cunaumJs", configuration.Voice.ElevenLabs.VoiceId);
    }

    [Fact]
    public async Task Voice_id_can_be_changed_and_persists()
    {
        var store = CreateStore();
        await store.LoadAsync();

        var updated = store.Current.Clone();
        updated.Voice.ElevenLabs.VoiceId = "SomeOtherVoiceId123";

        await store.SaveAsync(updated);

        // Re-read through a fresh store to prove it survived the round-trip
        // rather than only living in memory.
        var reloaded = await CreateStore().LoadAsync();

        Assert.Equal("SomeOtherVoiceId123", reloaded.Voice.ElevenLabs.VoiceId);
    }

    [Fact]
    public async Task Language_selection_persists_across_store_instances()
    {
        var store = CreateStore();
        await store.LoadAsync();

        var updated = store.Current.Clone();
        updated.General.Language = "ar";

        await store.SaveAsync(updated);

        var reloaded = await CreateStore().LoadAsync();

        Assert.Equal("ar", reloaded.General.Language);
    }

    [Fact]
    public async Task Save_round_trips_every_section()
    {
        var store = CreateStore();
        await store.LoadAsync();

        var updated = store.Current.Clone();
        updated.SetupCompleted = true;
        updated.General.Language = "ar";
        updated.General.Theme = AppTheme.Light;
        updated.Ai.Model = "claude-opus-5";
        updated.Ai.Effort = "xhigh";
        updated.Ai.MaxTokens = 32000;
        updated.Voice.SpeakResponsesAutomatically = true;
        updated.Voice.OutputVolume = 0.4;
        updated.Memory.IndexOnStartup = false;
        updated.Security.AutoApproveUpTo = ToolPermission.Write;

        await store.SaveAsync(updated);

        var reloaded = await CreateStore().LoadAsync();

        Assert.True(reloaded.SetupCompleted);
        Assert.Equal("ar", reloaded.General.Language);
        Assert.Equal(AppTheme.Light, reloaded.General.Theme);
        Assert.Equal("claude-opus-5", reloaded.Ai.Model);
        Assert.Equal("xhigh", reloaded.Ai.Effort);
        Assert.Equal(32000, reloaded.Ai.MaxTokens);
        Assert.True(reloaded.Voice.SpeakResponsesAutomatically);
        Assert.Equal(0.4, reloaded.Voice.OutputVolume);
        Assert.False(reloaded.Memory.IndexOnStartup);
        Assert.Equal(ToolPermission.Write, reloaded.Security.AutoApproveUpTo);
    }

    [Fact]
    public async Task Save_raises_the_changed_event()
    {
        var store = CreateStore();
        await store.LoadAsync();

        HammorConfiguration? observed = null;
        store.ConfigurationChanged += (_, configuration) => observed = configuration;

        var updated = store.Current.Clone();
        updated.General.Language = "ar";
        await store.SaveAsync(updated);

        Assert.NotNull(observed);
        Assert.Equal("ar", observed!.General.Language);
    }

    /// <summary>
    /// Clone must be deep: the settings UI edits a draft, and a shared nested
    /// object would let unsaved edits leak into the live configuration.
    /// </summary>
    [Fact]
    public void Clone_is_deep_for_nested_sections()
    {
        var original = new HammorConfiguration();
        var clone = original.Clone();

        clone.Voice.ElevenLabs.VoiceId = "changed";
        clone.General.Language = "ar";
        clone.Security.AutoApproveUpTo = ToolPermission.Execute;

        Assert.Equal("G3YpdjT1OTh9cunaumJs", original.Voice.ElevenLabs.VoiceId);
        Assert.Equal("en", original.General.Language);
        Assert.Equal(ToolPermission.Read, original.Security.AutoApproveUpTo);
    }

    /// <summary>
    /// A hand-edited, malformed config must not stop HAMMOR from starting, and
    /// must not be silently overwritten either.
    /// </summary>
    [Fact]
    public async Task Malformed_json_falls_back_to_defaults_and_preserves_the_file()
    {
        var paths = new HammorPaths(_root);
        paths.EnsureCreated();
        await File.WriteAllTextAsync(paths.ConfigurationFile, "{ this is not valid json ");

        var configuration = await CreateStore().LoadAsync();

        Assert.Equal("en", configuration.General.Language);

        // The bad file is left in place for the user to inspect.
        var onDisk = await File.ReadAllTextAsync(paths.ConfigurationFile);
        Assert.Contains("not valid json", onDisk, StringComparison.Ordinal);
    }

    /// <summary>
    /// The configuration model must never gain a credential field: keys live
    /// in DPAPI. Asserted on exact property names (walked recursively) rather
    /// than raw substrings, because legitimate names like <c>maxTokens</c>
    /// contain credential-ish fragments.
    /// </summary>
    [Fact]
    public async Task Secrets_are_never_written_to_the_configuration_file()
    {
        var store = CreateStore();
        await store.LoadAsync();
        await store.SaveAsync(store.Current.Clone());

        var json = await File.ReadAllTextAsync(new HammorPaths(_root).ConfigurationFile);

        string[] forbiddenNames =
            ["apikey", "api_key", "secret", "secretvalue", "password", "token", "accesstoken", "credential"];

        using var document = JsonDocument.Parse(json);

        var offenders = new List<string>();
        CollectPropertyNames(document.RootElement, offenders);

        var violations = offenders
            .Where(name => forbiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(violations);

        // Nothing in the file should look like a stored credential either.
        Assert.DoesNotContain("sk-ant-", json, StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectPropertyNames(JsonElement element, List<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    names.Add(property.Name);
                    CollectPropertyNames(property.Value, names);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPropertyNames(item, names);
                }

                break;
        }
    }

    private JsonConfigurationStore CreateStore() =>
        new(new HammorPaths(_root), NullLogger<JsonConfigurationStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
