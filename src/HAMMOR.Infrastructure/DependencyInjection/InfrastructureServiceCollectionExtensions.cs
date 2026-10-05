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
using HAMMOR.Infrastructure.Tasks;
using HAMMOR.Core.Storage;
using HAMMOR.Core.Tools.Filesystem;
using HAMMOR.Core.Tools.Git;
using HAMMOR.Core.Tools.Project;
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
        // ClaudeToolCallingProvider decorates ClaudeAiProvider: one instance is
        // registered under both contracts so the provider collection contains a
        // single "claude" entry that is also tool-capable. Registering the plain
        // provider as a second IAiProvider would make routing by id ambiguous.
        services.AddSingleton<ClaudeAiProvider>();
        services.AddSingleton<ClaudeToolCallingProvider>();
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<ClaudeToolCallingProvider>());
        services.AddSingleton<IToolCallingProvider>(sp => sp.GetRequiredService<ClaudeToolCallingProvider>());

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
        services.AddSingleton<AgentLoop>();

        // Unattended task execution (ADR-003). Registered but not started:
        // the host must call TaskSchedulerService.Start() explicitly.
        services.AddSingleton<ITaskRunner, TaskRunner>();
        services.AddSingleton<TaskSchedulerService>();
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

        // Filesystem tools are gated by IFilesystemPolicy when available.
        // Infrastructure is platform-neutral, so it registers a managed
        // fallback policy; Platform.Windows overrides it with a Windows-aware
        // one (WindowsPathResolution + same FilesystemPolicy).
        var policy = services.GetService<IFilesystemPolicy>()
            ?? new FilesystemPolicy(
                services.GetRequiredService<IConfigurationStore>(),
                services.GetRequiredService<HammorPaths>(),
                services.GetService<IPathResolution>() ?? new ManagedPathResolution());

        registry.Register(new ListDirectoryTool(policy));
        registry.Register(new ReadFileTool(policy));
        registry.Register(new WriteFileTool(policy));
        registry.Register(new DeleteFileTool(policy));

        // Git: structured, no arbitrary command executor. Only status/diff/log.
        var gitRunner = new SafeGitRunner(policy);
        registry.Register(new GitStatusTool(gitRunner));
        registry.Register(new GitDiffTool(gitRunner));
        registry.Register(new GitLogTool(gitRunner));

        // Project inspect: metadata-only, bounded, no content reads, no recursive scan.
        registry.Register(new ProjectInspectTool(policy));

        return registry;
    }
}
