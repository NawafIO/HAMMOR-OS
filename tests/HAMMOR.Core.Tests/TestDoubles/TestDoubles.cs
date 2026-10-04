using HAMMOR.Core.Configuration;
using HAMMOR.Core.Permissions;
using HAMMOR.Core.Tools;

namespace HAMMOR.Core.Tests.TestDoubles;

/// <summary>In-memory configuration store.</summary>
internal sealed class FakeConfigurationStore(HammorConfiguration configuration)
    : IConfigurationStore
{
    public HammorConfiguration Current { get; private set; } = configuration;

    public string ConfigurationFilePath => "(in-memory)";

    public event EventHandler<HammorConfiguration>? ConfigurationChanged;

    public Task<HammorConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task SaveAsync(
        HammorConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        Current = configuration;
        ConfigurationChanged?.Invoke(this, configuration);
        return Task.CompletedTask;
    }
}

/// <summary>Tool whose permission level and validity are set by the test.</summary>
internal sealed class FakeTool(
    string name,
    ToolPermission permission,
    bool isValid = true,
    bool succeeds = true) : ITool
{
    public string Name { get; } = name;

    public string Description => $"Test double for {Name}.";

    public ToolPermission Permission { get; } = permission;

    public ToolInputSchema InputSchema => ToolInputSchema.None;

    /// <summary>Number of times <see cref="ExecuteAsync"/> ran.</summary>
    public int ExecutionCount { get; private set; }

    public ToolValidationResult Validate(ToolInvocation invocation) =>
        isValid
            ? ToolValidationResult.Valid
            : ToolValidationResult.Invalid("Arguments rejected by the test double.");

    public Task<ToolResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ExecutionCount++;

        return Task.FromResult(succeeds
            ? ToolResult.Success($"{Name} ran.")
            : ToolResult.Failure($"{Name} failed."));
    }
}

/// <summary>Tool that throws, to verify unexpected faults stay diagnosable.</summary>
internal sealed class ThrowingTool(string name, ToolPermission permission) : ITool
{
    public string Name { get; } = name;

    public string Description => "Throws on execution.";

    public ToolPermission Permission { get; } = permission;

    public ToolInputSchema InputSchema => ToolInputSchema.None;

    public ToolValidationResult Validate(ToolInvocation invocation) =>
        ToolValidationResult.Valid;

    public Task<ToolResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Deliberate failure from the test double.");
}

/// <summary>
/// Confirmation service that answers a fixed way and records how often it was
/// consulted, so tests can assert the user was (or was not) interrupted.
/// </summary>
internal sealed class RecordingConfirmationService(bool approve) : IConfirmationService
{
    public int CallCount { get; private set; }

    public ConfirmationRequest? LastRequest { get; private set; }

    public Task<bool> RequestApprovalAsync(
        ConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(approve);
    }
}
