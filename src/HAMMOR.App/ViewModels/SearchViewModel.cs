using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HAMMOR.App.Search;
using Microsoft.Extensions.Logging;

namespace HAMMOR.App.ViewModels;

/// <summary>
/// Global Search: one box, results grouped by what they are, recent
/// conversations and projects before anything is typed.
/// </summary>
/// <remarks>
/// Typing waits briefly before searching, and a newer query cancels an
/// older one, so results always belong to what is in the box.
/// </remarks>
public sealed partial class SearchViewModel : ObservableObject
{
    /// <summary>How long typing must pause before a search starts.</summary>
    internal static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(180);

    private readonly SearchService _search;
    private readonly ILogger<SearchViewModel> _logger;

    private CancellationTokenSource? _pending;

    public SearchViewModel(SearchService search, ILogger<SearchViewModel> logger)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Results in display order; the page groups them by <see cref="SearchResult.GroupLabel"/>.</summary>
    public ObservableCollection<SearchResult> Results { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    private string _query = string.Empty;

    [ObservableProperty]
    private SearchResult? _selectedResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoResults))]
    [NotifyPropertyChangedFor(nameof(ShowNothingRecent))]
    private bool _isSearching;

    [ObservableProperty]
    private bool _someSourcesFailed;

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public bool ShowNoResults => HasQuery && !IsSearching && Results.Count == 0;

    public bool ShowNothingRecent => !HasQuery && !IsSearching && Results.Count == 0;

    /// <summary>Runs when the page opens: the current query again, or the recents.</summary>
    public Task OpenAsync() => RefreshAsync(waitForTyping: false);

    /// <summary>Moves the highlighted result, wrapping at the ends.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = SelectedResult is null ? -1 : Results.IndexOf(SelectedResult);
        var next = index < 0
            ? (delta > 0 ? 0 : Results.Count - 1)
            : ((index + delta) % Results.Count + Results.Count) % Results.Count;
        SelectedResult = Results[next];
    }

    partial void OnQueryChanged(string value) => _ = RefreshAsync(waitForTyping: true);

    private async Task RefreshAsync(bool waitForTyping)
    {
        _pending?.Cancel();
        _pending?.Dispose();
        var cancellation = new CancellationTokenSource();
        _pending = cancellation;

        try
        {
            if (waitForTyping)
            {
                await Task.Delay(Debounce, cancellation.Token).ConfigureAwait(true);
            }

            IsSearching = true;
            var outcome = await _search.SearchAsync(Query, cancellation.Token).ConfigureAwait(true);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            Results.Clear();
            foreach (var result in outcome.Results)
            {
                Results.Add(result);
            }

            SelectedResult = Results.FirstOrDefault();
            SomeSourcesFailed = outcome.SomeSourcesFailed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer query took over.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed.");
            SomeSourcesFailed = true;
        }
        finally
        {
            if (ReferenceEquals(_pending, cancellation))
            {
                IsSearching = false;
            }

            OnPropertyChanged(nameof(ShowNoResults));
            OnPropertyChanged(nameof(ShowNothingRecent));
        }
    }
}
