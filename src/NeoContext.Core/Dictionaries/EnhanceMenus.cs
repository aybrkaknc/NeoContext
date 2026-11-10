using System.Globalization;
using System.Reflection;

namespace NeoContext.Core.Dictionaries;

/// <summary>The dictionaries that ship inside NeoContext.</summary>
public static class EnhanceMenus
{
    private const string ResourceName = "NeoContext.Dictionaries.EnhanceMenusDic.xml";

    /// <summary>The ready-made menu groups for this PC and language.</summary>
    public static IReadOnlyList<EnhanceGroup> LoadEmbedded(DictionaryContext context)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"The embedded dictionary {ResourceName} is missing.");
        return EnhanceMenuParser.Parse(EnhanceMenuParser.Load(stream), context);
    }

    public static IReadOnlyList<EnhanceGroup> LoadEmbedded(CultureInfo culture) =>
        LoadEmbedded(DictionaryContext.ForCurrentSystem(culture));
}
