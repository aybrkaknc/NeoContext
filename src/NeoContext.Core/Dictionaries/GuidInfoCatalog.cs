using System.Globalization;

namespace NeoContext.Core.Dictionaries;

/// <summary>
/// A name and icon for a shell extension, which Windows does not give directly. The texts are tried in this
/// order: <see cref="ResText"/> (a string resource Windows already translates), <see cref="LocalizedText"/>
/// (the current language, then English), then <see cref="Text"/>.
/// </summary>
public sealed record GuidInfo(string? ResText, string? LocalizedText, string? Text, string? Icon)
{
    /// <summary>The candidates in priority order, for showing the first one that gives readable text.</summary>
    public IEnumerable<string?> Names => [ResText, LocalizedText, Text];
}

/// <summary>The name and icon dictionary for shell extensions (the INI comes from ContextMenuManager, GPL-3.0).</summary>
public sealed class GuidInfoCatalog
{
    private const string FileName = "GuidInfosDic.ini";

    private readonly Dictionary<Guid, GuidInfo> _entries;

    private GuidInfoCatalog(Dictionary<Guid, GuidInfo> entries) => _entries = entries;

    public static GuidInfoCatalog Empty { get; } = new([]);

    public int Count => _entries.Count;

    public GuidInfo? Find(Guid clsid) => _entries.GetValueOrDefault(clsid);

    public static GuidInfoCatalog LoadEmbedded(CultureInfo culture) => Load(DictionaryStore.EmbeddedOnly, culture);

    /// <summary>Reads the user's copy, the downloaded copy or the built-in one, in that order.</summary>
    public static GuidInfoCatalog Load(DictionaryStore store, CultureInfo culture)
    {
        using var stream = store.Open(FileName);
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), culture.Name);
    }

    /// <summary>Reads <c>[guid]</c> sections with <c>ResText</c>, <c>Text</c>, <c>&lt;culture&gt;-Text</c> and <c>Icon</c> lines.</summary>
    public static GuidInfoCatalog Parse(string ini, string culture)
    {
        var entries = new Dictionary<Guid, GuidInfo>();
        Guid? current = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Flush()
        {
            if (current is { } clsid)
            {
                var localized = values.GetValueOrDefault($"{culture}-Text") ?? values.GetValueOrDefault("en-US-Text");
                entries[clsid] = new GuidInfo(
                    values.GetValueOrDefault("ResText"),
                    localized,
                    values.GetValueOrDefault("Text"),
                    values.GetValueOrDefault("Icon"));
            }

            values.Clear();
        }

        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                current = Guid.TryParse(line[1..^1], out var parsed) ? parsed : null;
            }
            else if (current is not null && line.IndexOf('=') is var equals and > 0)
            {
                values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        }

        Flush();
        return new GuidInfoCatalog(entries);
    }
}
