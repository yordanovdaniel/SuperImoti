namespace SuperImoti.Web.Infrastructure;

/// <summary>
/// Builds the lower-cased text the search box matches against. SQLite's lower() and LIKE only
/// understand ASCII, so "надежда" would not find "Надежда" without this.
/// </summary>
public static class SearchTextBuilder
{
    public static string Build(params string?[] parts) =>
        string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()))
            .ToLowerInvariant();

    /// <summary>Splits a search query into lower-cased words (at most six).</summary>
    public static string[] Terms(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : query.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .Take(6)
                .ToArray();

    /// <summary>
    /// Turns the search box text into an SQLite full-text (FTS5) query: every word must appear,
    /// as a whole word or the beginning of one. "Лозен 2-стаен" → "лозен"* "2-стаен"*.
    /// Each word is quoted, so characters typed by the user cannot change the query syntax.
    /// </summary>
    public static string? ToFtsQuery(string? query)
    {
        var parts = Terms(query)
            .Where(term => term.Any(char.IsLetterOrDigit))
            .Select(term => "\"" + term.Replace("\"", "\"\"") + "\"*")
            .ToList();

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }
}
