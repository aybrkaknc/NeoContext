using NeoContext.Core.Models;
using NeoContext.Core.Rules;

namespace NeoContext.Tests.Core;

public sealed class ShellCommandsTests
{
    [Theory]
    [InlineData(MenuSection.Files, "\"%1\"")]
    [InlineData(MenuSection.FilesAndFolders, "\"%1\"")]
    [InlineData(MenuSection.Directory, "\"%V\"")]
    [InlineData(MenuSection.FolderBackground, "\"%V\"")]
    [InlineData(MenuSection.Desktop, "")]
    [InlineData(MenuSection.RecycleBin, "")]
    public void The_selection_token_depends_on_the_page(MenuSection section, string expected) =>
        Assert.Equal(expected, ShellCommands.SelectionToken(section));

    [Fact]
    public void A_direct_command_quotes_a_path_with_spaces_and_skips_empty_parts()
    {
        Assert.Equal("\"C:\\Program Files\\App\\app.exe\" -x \"%1\"", ShellCommands.Direct(@"C:\Program Files\App\app.exe", " -x ", "\"%1\""));
        Assert.Equal("notepad.exe", ShellCommands.Direct("notepad.exe", null, ""));
        Assert.Equal("\"C:\\a b\\x.exe\"", ShellCommands.Direct("\"C:\\a b\\x.exe\"", "", ""));
    }

    [Fact]
    public void The_script_never_contains_the_selection_token_and_reads_the_item_as_data()
    {
        var script = new NewVerbScript(@"C:\Tools\run.exe", "-v", "\"%1\"", Hidden: true, RunAsAdministrator: true);

        var content = ShellCommands.ScriptContent(script);

        Assert.StartsWith("param([string]$Target)", content);
        Assert.DoesNotContain("%1", content);
        Assert.Contains("FilePath = 'C:\\Tools\\run.exe'", content);
        Assert.Contains("$start.Verb = 'RunAs'", content);
        Assert.Contains("$start.WindowStyle = 'Hidden'", content);
    }

    [Fact]
    public void Quotes_in_fixed_arguments_are_doubled_so_they_cannot_end_the_string()
    {
        var script = new NewVerbScript("app.exe", "it's ‘x’", "", Hidden: false, RunAsAdministrator: false);

        var content = ShellCommands.ScriptContent(script);

        Assert.Contains("'it''s ‘‘x’’'", content);
        Assert.DoesNotContain("$start.Verb", content);
        Assert.DoesNotContain("WindowStyle", content);
    }

    [Fact]
    public void The_script_command_line_passes_only_the_clicked_item()
    {
        var withItem = ShellCommands.ScriptCommandLine(@"C:\S\a.ps1", new NewVerbScript("x.exe", "", "\"%V\"", true, false));
        var without = ShellCommands.ScriptCommandLine(@"C:\S\a.ps1", new NewVerbScript("x.exe", "", "", true, false));

        Assert.EndsWith("-File \"C:\\S\\a.ps1\" \"%V\"", withItem);
        Assert.EndsWith("-File \"C:\\S\\a.ps1\"", without);
    }

    [Fact]
    public void Key_names_are_letters_and_digits_and_unique()
    {
        var taken = new HashSet<string> { "OpenwithNotepad", "OpenwithNotepad2" };

        // Spaces and punctuation are dropped, letter case is kept.
        Assert.Equal("OpenwithNotepad3", ShellCommands.KeyName("Open with Notepad!", taken.Contains));
        Assert.Equal("Item", ShellCommands.KeyName("???", _ => false));
        Assert.Equal(40, ShellCommands.KeyName(new string('a', 100), _ => false).Length);
    }
}
