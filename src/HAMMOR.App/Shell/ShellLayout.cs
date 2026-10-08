using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HAMMOR.App.Shell;

/// <summary>
/// How the user left the shell: the part of the layout that is remembered
/// between launches.
/// </summary>
/// <remarks>
/// Deliberately a plain class with settable properties, so a file written by
/// an older or newer build still loads: properties it does not know are
/// ignored and missing ones keep their defaults.
/// </remarks>
public sealed class ShellLayout
{
    /// <summary>The newest layout this build understands.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>True when the user collapsed the sidebar to its icon rail.</summary>
    public bool SidebarCollapsed { get; set; }
}

/// <summary>
/// Reads and writes <see cref="ShellLayout"/> as a small JSON file in the
/// user's data folder.
/// </summary>
/// <remarks>
/// <para>
/// This is a convenience, never a requirement: a missing, unreadable, corrupt
/// or newer-than-known file loads as the defaults, and a failed save is logged
/// and reported through the return value rather than thrown, so a read-only
/// profile or a full disk can never stop the app from starting or closing a
/// sidebar. Only I/O and JSON failures are handled; anything else is a bug and
/// surfaces.
/// </para>
/// <para>
/// The file holds layout only: no secrets, paths or conversation content.
/// A save writes a sibling temporary file and moves it over the old one, so a
/// crash mid-write leaves the previous layout intact.
/// </para>
/// </remarks>
public sealed class ShellLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly ILogger _logger;

    public ShellLayoutStore(string path, ILogger<ShellLayoutStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The layout file needs a fully qualified path.", nameof(path));
        }

        _path = path;
        _logger = logger ?? NullLogger<ShellLayoutStore>.Instance;
    }

    /// <summary>The saved layout, or the defaults when there is none to use.</summary>
    public ShellLayout Load()
    {
        string json;
        try
        {
            if (!File.Exists(_path))
            {
                return new ShellLayout();
            }

            json = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The shell layout could not be read; using the defaults.");
            return new ShellLayout();
        }

        try
        {
            var layout = JsonSerializer.Deserialize<ShellLayout>(json, JsonOptions);
            if (layout is null || layout.Version < 1 || layout.Version > ShellLayout.CurrentVersion)
            {
                // Written by a build this one does not understand: do not guess.
                return new ShellLayout();
            }

            return layout;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "The shell layout file is not valid JSON; using the defaults.");
            return new ShellLayout();
        }
    }

    /// <summary>Saves the layout. Returns false, after logging, when it could not be written.</summary>
    public bool Save(ShellLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(layout, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "The shell layout could not be saved.");
            TryDelete(temporary);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done for a leftover temporary file.
        }
    }
}
