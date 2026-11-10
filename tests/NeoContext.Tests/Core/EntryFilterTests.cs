using NeoContext.Core.Models;
using NeoContext.Core.Rules;

namespace NeoContext.Tests.Core;

public sealed class EntryFilterTests
{
    private static ContextMenuEntry Entry(string name, MenuSection section, string key = "key", string? command = null) =>
        new($"{section}/{name}", EntryKind.ShellVerb, section, key, name, EntryState.Enabled, $@"HKCU\{name}", EntryCapabilities.Toggle)
        {
            Command = command,
        };

    private readonly ContextMenuEntry[] _entries =
    [
        Entry("Open with Code", MenuSection.Files, command: @"C:\Code\code.exe ""%1"""),
        Entry("Open in Terminal", MenuSection.Folders),
        Entry("Scan with Defender", MenuSection.FilesAndFolders, key: "EPP"),
        Entry("Zip", MenuSection.NewMenu),
    ];

    [Fact]
    public void Without_a_query_a_page_shows_only_its_own_section()
    {
        var shown = EntryFilter.Apply(_entries, "", MenuSection.Folders);

        Assert.Equal("Open in Terminal", Assert.Single(shown).DisplayName);
    }

    [Fact]
    public void Without_a_query_and_a_section_nothing_is_shown()
    {
        Assert.Empty(EntryFilter.Apply(_entries, null, null));
    }

    [Fact]
    public void A_query_searches_every_section_regardless_of_the_current_page()
    {
        var shown = EntryFilter.Apply(_entries, "open", MenuSection.NewMenu);

        Assert.Equal(["Open in Terminal", "Open with Code"], shown.Select(e => e.DisplayName));
    }

    [Theory]
    [InlineData("DEFENDER", "Scan with Defender")]
    [InlineData("epp", "Scan with Defender")]
    [InlineData("code.exe", "Open with Code")]
    [InlineData("  zip  ", "Zip")]
    public void Matching_ignores_case_and_looks_at_name_key_and_command(string query, string expected)
    {
        var shown = EntryFilter.Apply(_entries, query, MenuSection.Files);

        Assert.Equal(expected, Assert.Single(shown).DisplayName);
    }

    [Fact]
    public void A_query_with_no_match_gives_an_empty_list()
    {
        Assert.Empty(EntryFilter.Apply(_entries, "nothing-like-this", MenuSection.Files));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a", true)]
    public void Only_a_non_blank_query_counts_as_a_search(string? query, bool expected)
    {
        Assert.Equal(expected, EntryFilter.IsSearch(query));
    }
}
