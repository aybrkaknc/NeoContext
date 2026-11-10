using System.Text;
using NeoContext.Core.Dictionaries;

namespace NeoContext.Tests.Core;

public sealed class EnhanceMenuParserTests
{
    private static IReadOnlyList<EnhanceGroup> Parse(string body, string culture = "en-US", string os = "10.0.26100.0", Func<string, bool>? fileExists = null)
    {
        var xml = $"<Data><Group><Text Value='G'/><RegPath>HKEY_CLASSES_ROOT\\*</RegPath>{body}</Group></Data>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return EnhanceMenuParser.Parse(EnhanceMenuParser.Load(stream), new DictionaryContext(culture, Version.Parse(os), fileExists ?? (_ => true)));
    }

    private static EnhanceItem Single(string items, string culture = "en-US", string os = "10.0.26100.0", Func<string, bool>? fileExists = null) =>
        Assert.Single(Assert.Single(Parse($"<Shell>{items}</Shell>", culture, os, fileExists)).Items);

    private static string Value(EnhanceItem item, string name) => item.Node!.Values.Single(v => v.Name == name).Data;

    private const string Translated =
        "<Item KeyName='A'><Value>" +
        "<REG_SZ MUIVerb='zh' Icon='a.dll,1'/>" +
        "<REG_SZ MUIVerb='English'><Culture>en-US</Culture></REG_SZ>" +
        "<REG_SZ MUIVerb='Türkçe'><Culture>tr-TR</Culture></REG_SZ>" +
        "</Value></Item>";

    [Theory]
    [InlineData("tr-TR", "Türkçe")]
    [InlineData("en-US", "English")]
    [InlineData("de-DE", "English")]
    public void The_current_culture_wins_then_en_US_then_the_default(string culture, string expected)
    {
        var item = Single(Translated, culture);

        Assert.Equal(expected, item.Text);
        Assert.Equal("a.dll,1", item.Icon);
    }

    [Fact]
    public void Without_any_translation_the_default_text_is_used()
    {
        Assert.Equal("zh", Single("<Item KeyName='A'><Value><REG_SZ MUIVerb='zh'/></Value></Item>", "tr-TR").Text);
    }

    [Fact]
    public void Group_and_tip_texts_follow_the_culture_too()
    {
        var groups = Parse(
            "<Shell><Item KeyName='A'><Tip Value='zh tip'/><Tip Value='tr tip'><Culture>tr-TR</Culture></Tip><Value><REG_SZ MUIVerb='x'/></Value></Item></Shell>",
            "tr-TR");

        Assert.Equal("tr tip", groups[0].Items[0].Tip);
        Assert.Equal("G", groups[0].Text);
    }

    [Fact]
    public void OSVersion_conditions_hide_items_and_values()
    {
        const string items =
            "<Item KeyName='New'><OSVersion Compare='&gt;='>10.0</OSVersion><Value><REG_SZ MUIVerb='new'/></Value></Item>" +
            "<Item KeyName='Old'><OSVersion Compare='&lt;'>6.2</OSVersion><Value><REG_SZ MUIVerb='old'/></Value></Item>" +
            "<Item KeyName='Mixed'><Value><REG_SZ MUIVerb='m'/><REG_SZ Icon='i'><OSVersion Compare='&gt;='>6.2</OSVersion></REG_SZ><REG_SZ Extra='e'><OSVersion Compare='&lt;'>6.2</OSVersion></REG_SZ></Value></Item>";

        var parsed = Assert.Single(Parse($"<Shell>{items}</Shell>", os: "10.0.26100.0")).Items;

        Assert.Equal(["New", "Mixed"], parsed.Select(i => i.KeyName));
        Assert.Equal("i", parsed[1].Icon);
        Assert.DoesNotContain(parsed[1].Node!.Values, v => v.Name == "Extra");
    }

    [Fact]
    public void A_missing_file_hides_the_item()
    {
        const string item = "<Item KeyName='Gp'><FileExists>%SystemRoot%\\System32\\gpedit.msc</FileExists><Value><REG_SZ MUIVerb='x'/></Value></Item>";

        Assert.Empty(Parse($"<Shell>{item}</Shell>", fileExists: _ => false));
        Assert.Single(Assert.Single(Parse($"<Shell>{item}</Shell>", fileExists: _ => true)).Items);
    }

    [Fact]
    public void A_default_command_is_kept_as_it_is()
    {
        var item = Single("<Item KeyName='A'><Value><REG_SZ MUIVerb='x'/></Value><SubKey><Command Default='explorer.exe &quot;%v&quot;'/></SubKey></Item>");

        var command = Assert.Single(item.Node!.Children);
        Assert.Equal("command", command.Name);
        Assert.Equal("explorer.exe \"%v\"", command.Command!.Default);
    }

    [Fact]
    public void A_built_command_has_a_program_arguments_suffix_and_shell_execute()
    {
        var item = Single(
            "<Item KeyName='A'><Value><REG_SZ MUIVerb='x'/></Value><SubKey><Command>" +
            "<ShellExecute Verb='runas' WindowStyle='0'/><FileName>takeown.exe</FileName><Arguments Suffix=' /a'>/f \"%1\"</Arguments>" +
            "</Command></SubKey></Item>");

        var command = item.Node!.Children[0].Command!;
        Assert.Null(command.Default);
        Assert.Equal("takeown.exe", command.Program);
        Assert.Equal("/f \"%1\"", command.Arguments);
        Assert.Equal(" /a", command.Suffix);
        Assert.Equal(new ShellExecuteSpec("runas", 0), command.Execute);
    }

    [Fact]
    public void Script_text_keeps_its_line_breaks_and_loses_the_indentation()
    {
        var item = Single(
            "<Item KeyName='A'><Value><REG_SZ MUIVerb='x'/></Value><SubKey><Command><FileName>wscript.exe</FileName><Arguments>" +
            "<CreateFile FileName='Do.vbs' Content='\n          Dim wsh\n          Set wsh = Nothing'/></Arguments></Command></SubKey></Item>");

        var script = item.Node!.Children[0].Command!.ArgumentsScript!;

        Assert.Equal("Do.vbs", script.FileName);
        Assert.Equal("Dim wsh\r\nSet wsh = Nothing", script.Content);
    }

    [Fact]
    public void Nested_subkeys_and_dword_values_are_read_and_the_last_passing_duplicate_wins()
    {
        const string xml =
            "<Item KeyName='Menu'><Value><REG_SZ MUIVerb='m' SubCommands=''/><REG_DWORD Flag='4'/></Value><SubKey><Shell><SubKey>" +
            "<Item0><OSVersion Compare='&gt;='>6.2</OSVersion><Value><REG_SZ MUIVerb='modern'/></Value></Item0>" +
            "<Item0><OSVersion Compare='&lt;'>6.2</OSVersion><Value><REG_SZ MUIVerb='legacy'/></Value></Item0>" +
            "</SubKey></Shell></SubKey></Item>";

        var item = Single(xml);

        Assert.Equal(RegValueKind.DWord, item.Node!.Values.Single(v => v.Name == "Flag").Kind);
        var shell = Assert.Single(item.Node.Children);
        Assert.Equal("Shell", shell.Name);
        var child = Assert.Single(shell.Children);
        Assert.Equal("Item0", child.Name);
        Assert.Equal("modern", child.Values.Single().Data);
    }

    [Fact]
    public void A_handler_item_has_a_name_a_key_name_and_a_clsid()
    {
        var groups = Parse(
            "<ShellEx><Item><Text>@shell32.dll,-30304</Text><Icon>imageres.dll,-5304</Icon><KeyName>CopyTo</KeyName><Guid>c2fbb630-2971-11d1-a18c-00c04fd75d13</Guid></Item></ShellEx>");

        var item = Assert.Single(Assert.Single(groups).Items);
        Assert.Equal(EnhanceKind.Handler, item.Kind);
        Assert.Equal("CopyTo", item.KeyName);
        Assert.Equal("@shell32.dll,-30304", item.Text);
        Assert.Equal(Guid.Parse("c2fbb630-2971-11d1-a18c-00c04fd75d13"), item.Clsid);
    }

    [Fact]
    public void The_embedded_dictionary_loads_in_both_languages_with_translated_names()
    {
        var english = EnhanceMenus.LoadEmbedded(new DictionaryContext("en-US", new Version(10, 0, 26100), _ => true));
        var turkish = EnhanceMenus.LoadEmbedded(new DictionaryContext("tr-TR", new Version(10, 0, 26100), _ => true));

        Assert.Equal(11, english.Count);
        Assert.Equal(english.Count, turkish.Count);
        Assert.Equal("Copy Content to Clipboard", english.SelectMany(g => g.Items).Single(i => i.KeyName == "CopyContent").Text);
        Assert.Equal("İçeriği panoya kopyala", turkish.SelectMany(g => g.Items).Single(i => i.KeyName == "CopyContent").Text);
        Assert.Equal("Dosya", turkish[0].Text);
    }

    [Fact]
    public void Every_embedded_script_keeps_its_line_breaks()
    {
        var groups = EnhanceMenus.LoadEmbedded(new DictionaryContext("en-US", new Version(10, 0, 26100), _ => true));

        var scripts = groups.SelectMany(g => g.Items).Where(i => i.Node is not null)
            .SelectMany(i => Scripts(i.Node!)).ToList();

        Assert.NotEmpty(scripts);
        Assert.All(scripts, s => Assert.Contains("\r\n", s.Content));
    }

    private static IEnumerable<ScriptFile> Scripts(RegNode node) =>
        new[] { node.Command?.ProgramScript, node.Command?.ArgumentsScript }.OfType<ScriptFile>()
            .Concat(node.Children.SelectMany(Scripts));
}
