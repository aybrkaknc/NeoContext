using System.Globalization;
using System.Xml.Linq;

namespace NeoContext.Core.Dictionaries;

/// <summary>
/// One setting in the "Detailed editing" page. It is on or off according to its rules, which are checked
/// in order: the first rule whose value matches <see cref="On"/> or <see cref="Off"/> decides, and when no
/// rule matches the setting counts as on. A null <c>On</c> or <c>Off</c> means "the value does not exist".
/// </summary>
public sealed record DetailedRule(string RegPath, string ValueName, RegValueKind Kind, string? On, string? Off)
{
    /// <summary>Limits and the Windows default of a number setting.</summary>
    public int? Min { get; init; }

    public int? Max { get; init; }

    public int? Default { get; init; }
}

public enum DetailedKind
{
    /// <summary>A switch that writes the On or Off value of each rule.</summary>
    Toggle,

    /// <summary>A number the user types in (limits, counts).</summary>
    Number,

    /// <summary>A text the user types in.</summary>
    Text,
}

public sealed record DetailedItem(string Text, string? Tip, bool RestartExplorer, DetailedKind Kind, IReadOnlyList<DetailedRule> Rules);

/// <summary>A program or part of Windows with its settings. It is shown only when one of its CLSIDs is installed.</summary>
public sealed record DetailedGroup(string Text, string? Icon, IReadOnlyList<Guid> Guids, string RegPath, IReadOnlyList<DetailedItem> Items);

/// <summary>Reads the detailed editing dictionary (schema from ContextMenuManager, GPL-3.0). Groups kept in INI files are skipped.</summary>
public static class DetailedEdit
{
    private const string FileName = "DetailedEditDic.xml";

    public static IReadOnlyList<DetailedGroup> LoadEmbedded(DictionaryContext context) => Load(DictionaryStore.EmbeddedOnly, context);

    /// <summary>Reads the user's copy, the downloaded copy or the built-in one, in that order.</summary>
    public static IReadOnlyList<DetailedGroup> Load(DictionaryStore store, DictionaryContext context)
    {
        using var stream = store.Open(FileName);
        return Parse(EnhanceMenuParser.Load(stream), context);
    }

    public static IReadOnlyList<DetailedGroup> LoadEmbedded(CultureInfo culture) =>
        LoadEmbedded(DictionaryContext.ForCurrentSystem(culture));

    public static IReadOnlyList<DetailedGroup> Parse(XDocument document, DictionaryContext context)
    {
        var groups = new List<DetailedGroup>();
        foreach (var group in document.Root?.Elements("Group").Where(g => EnhanceMenuParser.Passes(g, context)) ?? [])
        {
            var path = group.Element("RegPath")?.Value.Trim();
            if (group.Element("IsIniGroup") is not null || string.IsNullOrEmpty(path))
            {
                continue;
            }

            var items = group.Elements("Item")
                .Where(i => EnhanceMenuParser.Passes(i, context))
                .Select(i => ParseItem(i, path, context))
                .OfType<DetailedItem>()
                .ToList();
            if (items.Count > 0)
            {
                groups.Add(new DetailedGroup(
                    EnhanceMenuParser.Pick(group.Elements("Text"), context, e => (string?)e.Attribute("Value")) ?? path,
                    EnhanceMenuParser.NullIfEmpty(group.Element("Icon")?.Value),
                    [.. group.Elements("Guid").Select(g => Guid.TryParse(g.Value.Trim(), out var clsid) ? clsid : (Guid?)null).OfType<Guid>()],
                    path,
                    items));
            }
        }

        return groups;
    }

    private static DetailedItem? ParseItem(XElement item, string groupPath, DictionaryContext context)
    {
        var text = EnhanceMenuParser.Pick(item.Elements("Text"), context, e => (string?)e.Attribute("Value"));
        var isText = item.Element("IsStringItem") is not null;
        var rules = item.Elements("Rule").Select(r => ParseRule(r, groupPath)).Select(r => isText ? r with { Kind = RegValueKind.Text } : r).ToList();
        if (string.IsNullOrWhiteSpace(text) || rules.Count == 0)
        {
            return null;
        }

        return new DetailedItem(
            text,
            EnhanceMenuParser.Pick(item.Elements("Tip"), context, e => (string?)e.Attribute("Value")),
            item.Element("RestartExplorer") is not null,
            item.Element("IsNumberItem") is not null ? DetailedKind.Number : item.Element("IsStringItem") is not null ? DetailedKind.Text : DetailedKind.Toggle,
            rules);
    }

    private static DetailedRule ParseRule(XElement rule, string groupPath)
    {
        // An empty path means the group's path; a path starting with a backslash is below the group's path.
        var path = (string?)rule.Attribute("RegPath");
        var full = string.IsNullOrEmpty(path) ? groupPath : path.StartsWith('\\') ? groupPath + path : path;
        var kind = ((string?)rule.Attribute("ValueKind"))?.ToUpperInvariant() switch
        {
            "REG_SZ" => RegValueKind.Text,
            "REG_EXPAND_SZ" => RegValueKind.ExpandString,
            "REG_BINARY" => RegValueKind.Binary,
            _ => RegValueKind.DWord,
        };

        int? Number(string name) => int.TryParse((string?)rule.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        return new DetailedRule(full, (string?)rule.Attribute("ValueName") ?? "", kind, (string?)rule.Attribute("On"), (string?)rule.Attribute("Off"))
        {
            Min = Number("Min"),
            Max = Number("Max"),
            Default = Number("Default"),
        };
    }
}
