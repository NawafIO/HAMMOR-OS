namespace HAMMOR.App.Search;

/// <summary>
/// Turns candidates from every source into the ordered, grouped list Search
/// shows. Pure: no stores, no UI.
/// </summary>
public static class SearchComposer
{
    /// <summary>The most results any one group shows.</summary>
    public static int CapFor(SearchResultKind kind) => kind switch
    {
        SearchResultKind.Page => 4,
        SearchResultKind.Setting => 4,
        _ => 6,
    };

    /// <summary>
    /// Scores every candidate against the query, drops what does not match,
    /// and orders the rest by group (the order of <see cref="SearchResultKind"/>),
    /// then by score, then newest first, then by title. Each group is capped.
    /// </summary>
    public static IReadOnlyList<SearchResult> Compose(string query, IEnumerable<SearchResult> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var folded = SearchText.Normalize(query);
        if (folded.Length == 0)
        {
            return [];
        }

        return candidates
            .Select(candidate => candidate with { Score = ScoreOf(folded, candidate) })
            .Where(candidate => candidate.Score > SearchText.NoMatch)
            .GroupBy(candidate => candidate.Kind)
            .OrderBy(group => group.Key)
            .SelectMany(group => group
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.When ?? DateTimeOffset.MinValue)
                .ThenBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(CapFor(group.Key)))
            .ToList();
    }

    /// <summary>
    /// What Search shows before anything is typed: the newest saved
    /// conversations, then the most recently opened projects.
    /// </summary>
    public static IReadOnlyList<SearchResult> Recent(
        IEnumerable<SearchResult> conversations,
        IEnumerable<SearchResult> projects)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(projects);

        return conversations
            .OrderByDescending(item => item.When ?? DateTimeOffset.MinValue)
            .Take(CapFor(SearchResultKind.Conversation))
            .Concat(projects.Take(CapFor(SearchResultKind.Project)))
            .ToList();
    }

    private static int ScoreOf(string folded, SearchResult candidate)
    {
        var score = SearchText.Score(folded, candidate.Title, candidate.Detail);
        return score == SearchText.NoMatch && candidate.MatchedByStore ? SearchText.DetailOnly : score;
    }
}
