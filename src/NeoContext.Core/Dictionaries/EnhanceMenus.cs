using System.Globalization;

namespace NeoContext.Core.Dictionaries;

/// <summary>The dictionaries that ship inside NeoContext.</summary>
public static class EnhanceMenus
{
    private const string FileName = "EnhanceMenusDic.xml";

    /// <summary>The ready-made menu groups for this PC and language.</summary>
    public static IReadOnlyList<EnhanceGroup> LoadEmbedded(DictionaryContext context) => Load(DictionaryStore.EmbeddedOnly, context);

    /// <summary>Reads the user's copy, the downloaded copy or the built-in one, in that order.</summary>
    public static IReadOnlyList<EnhanceGroup> Load(DictionaryStore store, DictionaryContext context)
    {
        using var stream = store.Open(FileName);
        return EnhanceMenuParser.Parse(EnhanceMenuParser.Load(stream), context);
    }

    public static IReadOnlyList<EnhanceGroup> LoadEmbedded(CultureInfo culture) =>
        LoadEmbedded(DictionaryContext.ForCurrentSystem(culture));
}
