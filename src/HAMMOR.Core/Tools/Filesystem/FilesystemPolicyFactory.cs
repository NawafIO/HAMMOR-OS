using HAMMOR.Core.Configuration;
using HAMMOR.Core.Storage;

namespace HAMMOR.Core.Tools.Filesystem;

/// <summary>
/// Helper for tests and tooling to build a policy that allows a specific
/// temporary root without touching the real DataRoot.
/// </summary>
public static class FilesystemPolicyFactory
{
    public static IFilesystemPolicy CreateForRoot(
        string allowedRoot,
        IPathResolution? resolution = null,
        List<string>? extraRoots = null)
    {
        var paths = new HammorPaths(allowedRoot);
        var config = new HammorConfiguration();
        if (extraRoots is not null)
        {
            config.Security.FilesystemAllowedRoots = new List<string>(extraRoots);
        }

        var store = new InMemoryConfigurationStore(config);
        return new FilesystemPolicy(store, paths, resolution);
    }

    private sealed class InMemoryConfigurationStore(HammorConfiguration configuration) : IConfigurationStore
    {
        public HammorConfiguration Current { get; } = configuration;
        public string ConfigurationFilePath => "(in-memory)";
        public event EventHandler<HammorConfiguration>? ConfigurationChanged;
        public Task<HammorConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(HammorConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
