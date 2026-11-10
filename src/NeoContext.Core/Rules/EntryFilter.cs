using NeoContext.Core.Models;

namespace NeoContext.Core.Rules;

/// <summary>
/// Decides which entries a page shows. A search is global: while the query is not empty it looks
/// through every section; with no query a page shows only its own section.
/// </summary>
public static class EntryFilter
{
    public static bool IsSearch(string? query) => !string.IsNullOrWhiteSpace(query);

    public static IReadOnlyList<ContextMenuEntry> Apply(IEnumerable<ContextMenuEntry> entries, string? query, MenuSection? section)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var text = query?.Trim() ?? "";
        IEnumerable<ContextMenuEntry> matches = text.Length > 0
            ? entries.Where(e => Matches(e, text))
            : section is { } only ? entries.Where(e => e.Section == only) : [];

        // A search crosses every section, so results are grouped by section first, then by name.
        return [.. matches
            .OrderBy(e => text.Length > 0 ? (int)e.Section : 0)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The name, description, registry key or CLSID, or the command line contains the text.</summary>
    public static bool Matches(ContextMenuEntry entry, string text) =>
        entry.DisplayName.Contains(text, StringComparison.CurrentCultureIgnoreCase)
        || (entry.Description?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || entry.Key.Contains(text, StringComparison.OrdinalIgnoreCase)
        || (entry.Command?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
}
