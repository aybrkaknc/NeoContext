using System.Xml;
using System.Xml.Linq;

namespace NeoContext.Core.Dictionaries;

/// <summary>
/// Reads the ready-made menu dictionary (the schema comes from ContextMenuManager, GPL-3.0).
/// <para>
/// Conditions: an element with an <c>OSVersion</c> child only counts on matching Windows versions and one
/// with a <c>FileExists</c> child only when the file exists. A <c>Culture</c> child marks a translation:
/// where several variants exist the current culture wins, then en-US, then the variant without culture.
/// </para>
/// </summary>
public static class EnhanceMenuParser
{
    /// <summary>
    /// Loads the XML without attribute-value normalization. The script texts are attribute values with line
    /// breaks, and a normalizing reader would turn every line break into a space and break the scripts.
    /// </summary>
    public static XDocument Load(Stream stream)
    {
        using var reader = new XmlTextReader(stream)
        {
            Normalization = false,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        return XDocument.Load(reader);
    }

    public static IReadOnlyList<EnhanceGroup> Parse(XDocument document, DictionaryContext context)
    {
        var groups = new List<EnhanceGroup>();
        foreach (var group in document.Root?.Elements("Group").Where(g => Passes(g, context)) ?? [])
        {
            var regPath = group.Element("RegPath")?.Value.Trim();
            if (string.IsNullOrEmpty(regPath))
            {
                continue;
            }

            var items = new List<EnhanceItem>();
            foreach (var item in group.Element("Shell")?.Elements("Item").Where(i => Passes(i, context)) ?? [])
            {
                if (ParseShellItem(item, context) is { } parsed)
                {
                    items.Add(parsed);
                }
            }

            foreach (var item in group.Element("ShellEx")?.Elements("Item").Where(i => Passes(i, context)) ?? [])
            {
                if (ParseHandlerItem(item, context) is { } parsed)
                {
                    items.Add(parsed);
                }
            }

            if (items.Count > 0)
            {
                groups.Add(new EnhanceGroup(
                    Pick(group.Elements("Text"), context, e => (string?)e.Attribute("Value") ?? NullIfEmpty(e.Value)) ?? regPath,
                    group.Element("Icon")?.Value.Trim(),
                    regPath,
                    items));
            }
        }

        return groups;
    }

    private static EnhanceItem? ParseShellItem(XElement item, DictionaryContext context)
    {
        var keyName = (string?)item.Attribute("KeyName");
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }

        var node = ParseNode(item, keyName, context);
        return new EnhanceItem(
            EnhanceKind.Verb,
            keyName,
            node.Values.FirstOrDefault(v => v.Name.Equals("MUIVerb", StringComparison.OrdinalIgnoreCase))?.Data ?? keyName,
            Pick(item.Elements("Tip"), context, e => (string?)e.Attribute("Value")),
            node.Values.FirstOrDefault(v => v.Name.Equals("Icon", StringComparison.OrdinalIgnoreCase))?.Data,
            node,
            null);
    }

    private static EnhanceItem? ParseHandlerItem(XElement item, DictionaryContext context)
    {
        var keyName = item.Element("KeyName")?.Value.Trim();
        if (string.IsNullOrEmpty(keyName) || !Guid.TryParse(item.Element("Guid")?.Value.Trim(), out var clsid))
        {
            return null;
        }

        return new EnhanceItem(
            EnhanceKind.Handler,
            keyName,
            Pick(item.Elements("Text"), context, e => NullIfEmpty(e.Value)) ?? keyName,
            Pick(item.Elements("Tip"), context, e => (string?)e.Attribute("Value")),
            NullIfEmpty(item.Element("Icon")?.Value),
            null,
            clsid);
    }

    /// <summary>The element's own values, plus one subkey per child of its <c>SubKey</c> element.</summary>
    private static RegNode ParseNode(XElement element, string name, DictionaryContext context)
    {
        var children = new List<RegNode>();
        foreach (var child in element.Element("SubKey")?.Elements().Where(c => Passes(c, context)) ?? [])
        {
            var childName = child.Name.LocalName;
            var node = childName.Equals("Command", StringComparison.OrdinalIgnoreCase)
                ? new RegNode("command", null, [], ParseCommand(child, context), [])
                : ParseNode(child, childName, context);

            // The dictionary repeats a name with different OSVersion conditions; the last one that passes wins.
            var existing = children.FindIndex(c => c.Name.Equals(node.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                children[existing] = node;
            }
            else
            {
                children.Add(node);
            }
        }

        return new RegNode(name, (string?)element.Attribute("Default"), ParseValues(element.Element("Value"), context), null, children);
    }

    private static List<RegValue> ParseValues(XElement? valueElement, DictionaryContext context)
    {
        var values = new List<RegValue>();
        var candidates = valueElement?.Elements().Where(e => e.Name.LocalName.StartsWith("REG_", StringComparison.Ordinal)) ?? [];
        foreach (var element in Layered(candidates, context))
        {
            var kind = element.Name.LocalName switch
            {
                "REG_DWORD" => RegValueKind.DWord,
                "REG_EXPAND_SZ" => RegValueKind.ExpandString,
                "REG_BINARY" => RegValueKind.Binary,
                _ => RegValueKind.Text,
            };

            // Each attribute is one value; a later (more specific) element overrides an earlier one.
            foreach (var attribute in element.Attributes())
            {
                var value = new RegValue(attribute.Name.LocalName, kind, attribute.Value);
                var existing = values.FindIndex(v => v.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    values[existing] = value;
                }
                else
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static CommandSpec ParseCommand(XElement command, DictionaryContext context)
    {
        var execute = command.Element("ShellExecute") is { } shell
            ? new ShellExecuteSpec(
                (string?)shell.Attribute("Verb"),
                int.TryParse((string?)shell.Attribute("WindowStyle"), out var style) ? style : null)
            : null;

        if ((string?)command.Attribute("Default") is { } whole)
        {
            return new CommandSpec(whole, "", null, "", null, "", "", execute);
        }

        var fileName = command.Element("FileName");
        var arguments = command.Element("Arguments");
        return new CommandSpec(
            null,
            TextOf(fileName),
            ScriptOf(fileName, context),
            TextOf(arguments),
            ScriptOf(arguments, context),
            (string?)arguments?.Attribute("Prefix") ?? "",
            (string?)arguments?.Attribute("Suffix") ?? "",
            execute);
    }

    /// <summary>The text directly inside the element, without the CreateFile children.</summary>
    private static string TextOf(XElement? element) =>
        element is null ? "" : string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();

    private static ScriptFile? ScriptOf(XElement? element, DictionaryContext context)
    {
        var file = element is null ? null : Layered(element.Elements("CreateFile"), context).LastOrDefault();
        var name = (string?)file?.Attribute("FileName");
        return file is null || string.IsNullOrWhiteSpace(name) ? null : new ScriptFile(name, Dedent((string?)file.Attribute("Content") ?? ""));
    }

    /// <summary>Removes the indentation the XML adds to every script line and the blank lines around the script.</summary>
    private static string Dedent(string content)
    {
        var lines = content.Replace("\r", "", StringComparison.Ordinal).Split('\n').Select(l => l.Trim()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join("\r\n", lines);
    }

    internal static bool Passes(XElement element, DictionaryContext context) =>
        element.Elements("OSVersion").All(v => VersionPasses(v, context))
        && element.Elements("FileExists").All(f => context.FileExists(f.Value.Trim()));

    private static bool VersionPasses(XElement element, DictionaryContext context)
    {
        if (!Version.TryParse(element.Value.Trim(), out var wanted))
        {
            return true;
        }

        var order = context.OsVersion.CompareTo(wanted);
        return ((string?)element.Attribute("Compare") ?? "=") switch
        {
            ">=" => order >= 0,
            ">" => order > 0,
            "<=" => order <= 0,
            "<" => order < 0,
            _ => order == 0,
        };
    }

    /// <summary>
    /// The elements that pass their conditions, lowest priority first: no culture, then en-US, then the
    /// current culture. Reading them in this order and letting later ones override gives the right text.
    /// </summary>
    private static IEnumerable<XElement> Layered(IEnumerable<XElement> elements, DictionaryContext context)
    {
        var passing = elements.Where(e => Passes(e, context)).ToList();
        var current = context.Culture;
        return passing.Where(e => CultureOf(e) is null)
            .Concat(passing.Where(e => IsCulture(e, "en-US")))
            .Concat(passing.Where(e => !current.Equals("en-US", StringComparison.OrdinalIgnoreCase) && IsCulture(e, current)));
    }

    internal static string? Pick(IEnumerable<XElement> elements, DictionaryContext context, Func<XElement, string?> read) =>
        Layered(elements, context).Select(read).LastOrDefault(v => v is not null);

    private static string? CultureOf(XElement element) => NullIfEmpty(element.Element("Culture")?.Value);

    private static bool IsCulture(XElement element, string culture) =>
        CultureOf(element)?.Equals(culture, StringComparison.OrdinalIgnoreCase) == true;

    internal static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
