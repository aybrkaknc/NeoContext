using System.Xml;
using System.Xml.Linq;

namespace NeoContext.Uwp;

/// <summary>One context menu verb declared in an app manifest.</summary>
/// <param name="Clsid">The COM class that implements the verb.</param>
/// <param name="VerbId">The verb id from the manifest.</param>
/// <param name="ItemType">What the verb applies to: <c>*</c>, <c>Directory</c>, <c>.txt</c>, ...</param>
/// <param name="ClassDisplayName">The COM class display name, when the manifest gives a plain one.</param>
public sealed record ManifestVerb(Guid Clsid, string VerbId, string ItemType, string? ClassDisplayName);

/// <summary>
/// Reads <c>windows.fileExplorerContextMenus</c> declarations from an AppxManifest.xml.
/// <para>
/// The original app looked packages up with a prefix match on the package name, which picked the
/// wrong package when names shared a prefix. Reading each installed package's own manifest ties
/// every verb to exactly one package.
/// </para>
/// </summary>
public static class AppxManifestReader
{
    private const string ContextMenuCategory = "windows.fileExplorerContextMenus";

    /// <summary>
    /// Parses the verbs. Matching is by local element name, because the elements appear under
    /// several namespace versions (desktop4, desktop5, desktop10, ...).
    /// </summary>
    public static IReadOnlyList<ManifestVerb> ReadContextMenuVerbs(XDocument manifest)
    {
        var classNames = manifest.Descendants()
            .Where(e => e.Name.LocalName == "Class" && Guid.TryParse((string?)e.Attribute("Id"), out _))
            .GroupBy(e => Guid.Parse((string)e.Attribute("Id")!))
            .ToDictionary(g => g.Key, g => (string?)g.First().Attribute("DisplayName"));

        var verbs = new List<ManifestVerb>();
        var extensions = manifest.Descendants()
            .Where(e => e.Name.LocalName == "Extension" && (string?)e.Attribute("Category") == ContextMenuCategory);

        foreach (var extension in extensions)
        {
            foreach (var itemType in extension.Descendants().Where(e => e.Name.LocalName == "ItemType"))
            {
                var type = (string?)itemType.Attribute("Type") ?? "*";
                foreach (var verb in itemType.Elements().Where(e => e.Name.LocalName == "Verb"))
                {
                    if (!Guid.TryParse((string?)verb.Attribute("Clsid"), out var clsid))
                    {
                        continue;
                    }

                    classNames.TryGetValue(clsid, out var className);
                    verbs.Add(new ManifestVerb(clsid, (string?)verb.Attribute("Id") ?? "", type, PlainText(className)));
                }
            }
        }

        return verbs;
    }

    /// <summary>Reads the manifest file in an install folder. Returns no verbs when it is missing or invalid.</summary>
    public static IReadOnlyList<ManifestVerb> ReadFromInstallFolder(string installPath)
    {
        var path = Path.Combine(installPath, "AppxManifest.xml");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return ReadContextMenuVerbs(XDocument.Load(path));
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>"ms-resource:" names need the package's resource index; those are left out.</summary>
    private static string? PlainText(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? null : name;
}
