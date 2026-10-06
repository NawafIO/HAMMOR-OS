using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAMMOR.Core.Audit;
using HAMMOR.Core.Memory;
using HAMMOR.Core.Projects;
using HAMMOR.Core.Storage;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.ViewModels;

/// <summary>Shows the audit trail.</summary>
public sealed partial class ActivityViewModel(
    IAuditLog auditLog,
    ILogger<ActivityViewModel> logger) : ObservableObject
{
    private readonly IAuditLog _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));

    private readonly ILogger<ActivityViewModel> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public ObservableCollection<AuditEntry> Entries { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    public bool IsEmpty => Entries.Count == 0 && !IsLoading;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var entries = await _auditLog.GetRecentAsync(200).ConfigureAwait(true);

            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the audit trail.");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}

/// <summary>Browses and searches memory.</summary>
public sealed partial class MemoryViewModel(
    IMemoryStore memoryStore,
    ISemanticMemoryIndex semanticIndex,
    HammorPaths paths,
    ILogger<MemoryViewModel> logger) : ObservableObject
{
    private readonly IMemoryStore _memoryStore =
        memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));

    private readonly ISemanticMemoryIndex _semanticIndex =
        semanticIndex ?? throw new ArgumentNullException(nameof(semanticIndex));

    private readonly HammorPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    private readonly ILogger<MemoryViewModel> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public ObservableCollection<MemoryEntry> Entries { get; } = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasSearched;

    /// <summary>
    /// False in Phase 1. Surfaced so the page can state that search is
    /// keyword-based rather than implying semantic retrieval.
    /// </summary>
    public bool IsSemanticSearchAvailable => _semanticIndex.IsAvailable;

    public bool IsEmpty => Entries.Count == 0 && !IsLoading;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        HasSearched = false;

        try
        {
            var entries = await _memoryStore.GetRecentAsync(limit: 100).ConfigureAwait(true);
            Replace(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load memory entries.");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchQuery.Trim();

        if (query.Length == 0)
        {
            await LoadAsync().ConfigureAwait(true);
            return;
        }

        IsLoading = true;

        try
        {
            var entries = await _memoryStore.SearchAsync(query, limit: 100).ConfigureAwait(true);
            Replace(entries);
            HasSearched = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory search failed for '{Query}'.", query);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Opens the markdown folder so memory can be read outside HAMMOR.</summary>
    [RelayCommand]
    private void OpenMemoryFolder()
    {
        try
        {
            Directory.CreateDirectory(_paths.MemoryDirectory);

            // UseShellExecute launches Explorer; the path is HAMMOR's own
            // data folder, never user-supplied input.
            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.MemoryDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open the memory folder.");
        }
    }

    private void Replace(IReadOnlyList<MemoryEntry> entries)
    {
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }
    }
}

/// <summary>Lists projects.</summary>
public sealed partial class ProjectsViewModel(
    IProjectStore projectStore,
    ILogger<ProjectsViewModel> logger) : ObservableObject
{
    private readonly IProjectStore _projectStore =
        projectStore ?? throw new ArgumentNullException(nameof(projectStore));

    private readonly ILogger<ProjectsViewModel> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public ObservableCollection<HammorProject> Projects { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    public bool IsEmpty => Projects.Count == 0 && !IsLoading;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var projects = await _projectStore.ListAsync().ConfigureAwait(true);

            Projects.Clear();
            foreach (var project in projects)
            {
                Projects.Add(project);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load projects.");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
