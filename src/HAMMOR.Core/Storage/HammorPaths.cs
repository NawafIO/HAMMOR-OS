namespace HAMMOR.Core.Storage;

/// <summary>
/// Resolves where HAMMOR keeps its data. Everything is per-user under
/// LocalApplicationData so nothing lands in the repository or in a shared
/// location.
/// </summary>
public sealed class HammorPaths
{
    private const string FolderName = "HAMMOR";

    /// <summary>
    /// Creates a path set rooted at <paramref name="dataRoot"/>, or at the
    /// per-user default when it is null or blank.
    /// </summary>
    public HammorPaths(string? dataRoot = null)
    {
        DataRoot = string.IsNullOrWhiteSpace(dataRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FolderName)
            : Path.GetFullPath(dataRoot);
    }

    /// <summary>Root folder for all HAMMOR state.</summary>
    public string DataRoot { get; }

    /// <summary>Configuration file. Git-ignored; contains no secrets.</summary>
    public string ConfigurationFile => Path.Combine(DataRoot, "hammor.config.json");

    /// <summary>SQLite database for structured state.</summary>
    public string DatabaseFile => Path.Combine(DataRoot, "hammor.db");

    /// <summary>DPAPI-encrypted secret blobs, one file per secret.</summary>
    public string SecretsDirectory => Path.Combine(DataRoot, "secrets");

    /// <summary>Human-readable markdown memory mirror.</summary>
    public string MemoryDirectory => Path.Combine(DataRoot, "memory");

    /// <summary>Rolling log files.</summary>
    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>
    /// Creates every directory HAMMOR writes to. Idempotent and safe to call
    /// on each launch.
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(SecretsDirectory);
        Directory.CreateDirectory(MemoryDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
