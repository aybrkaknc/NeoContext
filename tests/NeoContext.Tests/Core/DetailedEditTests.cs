using System.Text;
using System.Text.RegularExpressions;
using NeoContext.Core.Dictionaries;

namespace NeoContext.Tests.Core;

public sealed partial class DetailedEditTests
{
    private static IReadOnlyList<DetailedGroup> Parse(string xml, string culture = "en-US")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"<Data>{xml}</Data>"));
        return DetailedEdit.Parse(EnhanceMenuParser.Load(stream), new DictionaryContext(culture, new Version(10, 0, 26100), _ => true));
    }

    private static DetailedItem OnlyItem(string itemXml, string culture = "en-US") =>
        Assert.Single(Assert.Single(Parse($"<Group><Text Value='G'/><RegPath>HKEY_CURRENT_USER\\Software\\G</RegPath>{itemXml}</Group>", culture)).Items);

    [Fact]
    public void Rule_paths_are_resolved_against_the_group_path()
    {
        var item = OnlyItem(
            "<Item><Text Value='x'/>" +
            "<Rule ValueName='A'/>" +
            "<Rule RegPath='\\Sub' ValueName='B'/>" +
            "<Rule RegPath='HKEY_LOCAL_MACHINE\\SOFTWARE\\Other' ValueName='C'/></Item>");

        Assert.Equal(
            [@"HKEY_CURRENT_USER\Software\G", @"HKEY_CURRENT_USER\Software\G\Sub", @"HKEY_LOCAL_MACHINE\SOFTWARE\Other"],
            item.Rules.Select(r => r.RegPath));
    }

    [Fact]
    public void A_missing_On_or_Off_attribute_means_the_value_must_not_exist_and_the_kind_defaults_to_DWord()
    {
        var item = OnlyItem(
            "<Item><Text Value='x'/><Rule ValueName='A' On='1'/><Rule ValueName='B' Off='x' ValueKind='REG_SZ'/><Rule ValueName='C' On='00 01' ValueKind='REG_BINARY'/></Item>");

        Assert.Equal(new DetailedRule(@"HKEY_CURRENT_USER\Software\G", "A", RegValueKind.DWord, "1", null), item.Rules[0]);
        Assert.Equal(new DetailedRule(@"HKEY_CURRENT_USER\Software\G", "B", RegValueKind.Text, null, "x"), item.Rules[1]);
        Assert.Equal(RegValueKind.Binary, item.Rules[2].Kind);
    }

    [Fact]
    public void Item_kinds_flags_and_translations_are_read()
    {
        const string xml =
            "<Item><Text Value='zh'/><Text Value='Hello'><Culture>en-US</Culture></Text><Text Value='Merhaba'><Culture>tr-TR</Culture></Text>" +
            "<Tip Value='tip'/><RestartExplorer/><Rule ValueName='A'/></Item>";

        Assert.Equal("Merhaba", OnlyItem(xml, "tr-TR").Text);
        var english = OnlyItem(xml);
        Assert.Equal("Hello", english.Text);
        Assert.Equal("tip", english.Tip);
        Assert.True(english.RestartExplorer);
        Assert.Equal(DetailedKind.Toggle, english.Kind);
        Assert.Equal(DetailedKind.Number, OnlyItem("<Item><Text Value='n'/><IsNumberItem/><Rule ValueName='N' Max='9'/></Item>").Kind);
        Assert.Equal(DetailedKind.Text, OnlyItem("<Item><Text Value='t'/><IsStringItem/><Rule ValueName='T'/></Item>").Kind);
    }

    [Fact]
    public void Items_without_rules_or_text_and_ini_groups_are_skipped_and_guids_are_read()
    {
        var groups = Parse(
            "<Group><Text Value='A'/><Guid>20D04FE0-3AEA-1069-A2D8-08002B30309D</Guid><Guid>not a guid</Guid><RegPath>HKEY_CURRENT_USER\\A</RegPath>" +
            "<Item><Text Value='ok'/><Rule ValueName='V'/></Item><Item><Text Value='no rules'/></Item><Item><Rule ValueName='V'/></Item></Group>" +
            "<Group><Text Value='Ini'/><IsIniGroup/><FilePath>%AppData%\\x.ini</FilePath><Item><Text Value='i'/><Rule Section='S' KeyName='K'/></Item></Group>");

        var group = Assert.Single(groups);
        Assert.Equal([Guid.Parse("20D04FE0-3AEA-1069-A2D8-08002B30309D")], group.Guids);
        Assert.Equal("ok", Assert.Single(group.Items).Text);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void The_embedded_dictionary_has_no_untranslated_text(string culture)
    {
        var groups = DetailedEdit.LoadEmbedded(new DictionaryContext(culture, new Version(10, 0, 26100), _ => true));

        Assert.Equal(12, groups.Count); // 13 groups, minus the one kept in an INI file
        var texts = groups.SelectMany(g => new[] { g.Text }.Concat(g.Items.SelectMany(i => new[] { i.Text, i.Tip }))).OfType<string>();
        Assert.All(texts, text => Assert.DoesNotMatch(Chinese(), text));
    }

    [Fact]
    public void The_embedded_dictionary_has_toggle_number_and_text_items_with_rules()
    {
        var groups = DetailedEdit.LoadEmbedded(new DictionaryContext("tr-TR", new Version(10, 0, 26100), _ => true));
        var items = groups.SelectMany(g => g.Items).ToList();

        Assert.True(items.Count > 70);
        Assert.All(items, i => Assert.NotEmpty(i.Rules));
        Assert.Contains(items, i => i.Kind == DetailedKind.Number);
        Assert.Contains(items, i => i.Text == "Bağlam menüsü" && i.RestartExplorer);
    }

    [Fact]
    public void The_guid_catalog_prefers_resource_strings_then_the_current_language_then_english_then_the_default()
    {
        const string ini =
            "; comment\n" +
            "[11111111-1111-1111-1111-111111111111]\nResText = @shell32.dll,-8770\nText = zh\nen-US-Text = English\ntr-TR-Text = Türkçe\nIcon = imageres.dll, -109\n" +
            "[22222222-2222-2222-2222-222222222222]\nText = zh only\nen-US-Text = English only\n" +
            "[not-a-guid]\nText = ignored\n";

        var turkish = GuidInfoCatalog.Parse(ini, "tr-TR").Find(Guid.Parse("11111111-1111-1111-1111-111111111111"))!;
        Assert.Equal(["@shell32.dll,-8770", "Türkçe", "zh"], turkish.Names);
        Assert.Equal("imageres.dll, -109", turkish.Icon);

        Assert.Equal("English only", GuidInfoCatalog.Parse(ini, "tr-TR").Find(Guid.Parse("22222222-2222-2222-2222-222222222222"))!.LocalizedText);
        Assert.Equal("English", GuidInfoCatalog.Parse(ini, "en-US").Find(Guid.Parse("11111111-1111-1111-1111-111111111111"))!.LocalizedText);
        Assert.Equal(2, GuidInfoCatalog.Parse(ini, "en-US").Count);
    }

    [Fact]
    public void The_embedded_guid_catalog_knows_windows_extensions()
    {
        var catalog = GuidInfoCatalog.LoadEmbedded(new System.Globalization.CultureInfo("tr-TR"));

        Assert.True(catalog.Count > 200);
        Assert.Equal("@shell32.dll,-8770", catalog.Find(Guid.Parse("20d04fe0-3aea-1069-a2d8-08002b30309d"))!.ResText);
        Assert.Null(catalog.Find(Guid.NewGuid()));
    }

    [GeneratedRegex(@"[一-鿿]")]
    private static partial Regex Chinese();
}
