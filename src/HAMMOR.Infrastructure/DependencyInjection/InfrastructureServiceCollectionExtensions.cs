using HAMMOR.Core.Agent;
using HAMMOR.Core.Ai;
using HAMMOR.Core.Audit;
using HAMMOR.Core.Configuration;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Status;
using HAMMOR.Core.Tasks;
using HAMMOR.Core.Tools;
using HAMMOR.Core.Voice;
using HAMMOR.Infrastructure.Ai;
using HAMMOR.Infrastructure.Configuration;
using HAMMOR.Infrastructure.Memory;
using HAMMOR.Infrastructure.Persistence;
using HAMMOR.Infrastructure.Status;
using HAMMOR.Core.Storage;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Infrastructure.Tools;
using HAMMOR.Infrastructure.Voice;
using Microsoft.Extensions.DependencyInjection;

namespace HAMMOR.Infrastructure.DependencyInjection;

/// <summary>
/// Wires Core abstractions to their concrete implementations.
/// </summary>
/// <remarks>
/// The platform layer (DPAPI secrets, NAudio devices) and the UI layer
/// (confirmation dialogs, localisation) register their own services; this
/// method covers everything that is platform-neutral.
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers configuration, persistence, providers, tools and the agent
    /// pipeline.
    /// </summary>
    /// <param name="services">Container to populate.</param>
    /// <param name="dataRoot">
    /// Storage root, or null for the per-user default.
    /// </param>
    public static IServiceCollection AddHammorInfrastructure(
        this IServiceCollection services,
        string? dataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // --- Storage + configuration -------------------------------------
        services.AddSingleton(new HammorPaths(dataRoot));
        services.AddSingleton<IConfigurationStore, JsonConfigurationStore>();

        // --- Persistence --------------------------------------------------
        services.AddSingleton<SqliteDatabase>();
        services.AddSingleton<IAuditLog, SqliteAuditLog>();
        services.AddSingleton<ITaskStore, SqliteTaskStore>();
        services.AddSingleton<IProjectStore, SqliteProjectStore>();

        // Memory: SQLite is authoritative, wrapped so each save also writes
        // the human-readable markdown mirror.
        services.AddSingleton<SqliteMemoryStore>();
        services.AddSingleton<MarkdownMemoryMirror>();
        services.AddSingleton<MarkdownMemoryIndexer>();
        services.AddSingleton<IMemoryStore>(sp => new MarkdownMirroringMemoryStore(
            sp.GetRequiredService<SqliteMemoryStore>(),
            sp.GetRequiredService<MarkdownMemoryMirror>()));

        // NOT IMPLEMENTED in Phase 1 — abstraction only.
        services.AddSingleton<ISemanticMemoryIndex, UnavailableSemanticMemoryIndex>();

        // --- AI providers -------------------------------------------------
        // Registered as a collection so additional providers can be added
        // without changing the pipeline; routing is by configured id.
        services.AddSingleton<IAiProvider, ClaudeAiProvider>();

        // --- Voice providers ----------------------------------------------
        services.AddHttpClient(ElevenLabsTextToSpeechProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(ElevenLabsTextToSpeechProvider.BaseAddress);
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddSingleton<ITextToSpeechProvider, ElevenLabsTextToSpeechProvider>();

        // NOT IMPLEMENTED in Phase 1 — reports itself unavailable.
        services.AddSingleton<ISpeechToTextProvider, UnavailableSpeechToTextProvider>();

        services.AddSingleton<IVoiceOrchestrator, VoiceOrchestrator>();

        // --- Security + agent loop ----------------------------------------
        services.AddSingleton<IPermissionEvaluator, PermissionEvaluator>();
        services.AddSingleton<IToolRegistry>(BuildToolRegistry);
        services.AddSingleton<IAgentPipeline, AgentPipeline>();
        services.AddSingleton<ISystemStatusService, SystemStatusService>();

        return services;
    }

    /// <summary>
    /// Builds the tool registry. Phase 1 ships a deliberately small set — two
    /// real memory tools — rather than a broad surface. Notably absent is any
    /// arbitrary-command tool; see <see cref="ITool"/>.
    /// </summary>
    private static IToolRegistry BuildToolRegistry(IServiceProvider services)
    {
        var registry = new ToolRegistry();
        var memoryStore = services.GetRequiredService<IMemoryStore>();

        registry.Register(new SaveMemoryTool(memoryStore));
        registry.Register(new SearchMemoryTool(memoryStore));
        registry.Register(new ListDirectoryTool());
        registry.Register(new ReadFileTool());
        registry.Register(new WriteFileTool());
        registry.Register(new DeleteFileTool());

        return registry;
    }
}
