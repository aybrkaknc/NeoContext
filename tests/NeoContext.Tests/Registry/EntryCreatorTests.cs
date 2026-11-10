using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>Creating verbs, submenus, scripted verbs and handlers in a sandbox key.</summary>
public sealed class EntryCreatorTests : IDisposable
{
    private readonly RegistrySandbox _sandbox = new();
    private readonly string _scripts = Path.Combine(Path.GetTempPath(), $"NeoContext.Tests.{Guid.NewGuid():N}");

    public void Dispose()
    {
        _sandbox.Dispose();
        if (Directory.Exists(_scripts))
        {
            Directory.Delete(_scripts, recursive: true);
        }
    }

    private EntryCreator Creator() => new(_sandbox.Classes, _scripts);

    private static NewVerb Verb(string name, string? command = null, NewVerbScript? script = null, params NewVerbChild[] children) =>
        new(name, command, script, null, new VerbAttributes(), children);

    private string? Value(string relativePath, string name)
    {
        using var key = RegistryStore.OpenRead(_sandbox.Root / relativePath);
        return key?.GetValue(name) as string;
    }

    [Fact]
    public void A_single_verb_is_written_with_its_name_and_command_and_listed_back()
    {
        var result = Creator().CreateVerb(MenuSection.Files, Verb("Open in Notepad", "notepad.exe \"%1\""), allUsers: false);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("Open in Notepad", Value(@"*\shell\OpeninNotepad", "MUIVerb"));
        Assert.Equal("notepad.exe \"%1\"", Value(@"*\shell\OpeninNotepad\command", ""));

        var entry = Assert.Single(new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance).Load().Value, e => e.Key == "OpeninNotepad");
        Assert.Equal(MenuSection.Files, entry.Section);
        Assert.Equal("Open in Notepad", entry.DisplayName);
    }

    [Fact]
    public void A_second_verb_with_the_same_name_gets_a_numbered_key()
    {
        Assert.True(Creator().CreateVerb(MenuSection.Directory, Verb("Tool", "a.exe"), false).IsSuccess);
        Assert.True(Creator().CreateVerb(MenuSection.Directory, Verb("Tool", "b.exe"), false).IsSuccess);

        Assert.Equal("a.exe", Value(@"Directory\shell\Tool\command", ""));
        Assert.Equal("b.exe", Value(@"Directory\shell\Tool2\command", ""));
    }

    [Fact]
    public void A_submenu_from_shared_commands_lists_their_names_and_has_no_children()
    {
        var verb = Verb("Shared tools") with { SharedCommands = ["Windows.Scan", "Windows.Clean"] };

        Assert.True(Creator().CreateVerb(MenuSection.Directory, verb, false).IsSuccess);

        Assert.Equal("Windows.Scan;Windows.Clean", Value(@"Directory\shell\Sharedtools", "SubCommands"));
        Assert.False(_sandbox.Exists(@"Directory\shell\Sharedtools\shell"));
        Assert.False(_sandbox.Exists(@"Directory\shell\Sharedtools\command"));
    }

    [Fact]
    public void A_submenu_gets_an_empty_SubCommands_value_and_its_children()
    {
        var verb = Verb("Tools", null, null, new NewVerbChild("Scan", "scan.exe"), new NewVerbChild("Clean", "clean.exe"));

        Assert.True(Creator().CreateVerb(MenuSection.FolderBackground, verb, false).IsSuccess);

        Assert.Equal("", Value(@"Directory\Background\shell\Tools", "SubCommands"));
        Assert.Equal("Scan", Value(@"Directory\Background\shell\Tools\shell\Scan", "MUIVerb"));
        Assert.Equal("clean.exe", Value(@"Directory\Background\shell\Tools\shell\Clean\command", ""));
        Assert.False(_sandbox.Exists(@"Directory\Background\shell\Tools\command"));
    }

    [Fact]
    public void A_scripted_verb_writes_the_script_file_and_points_the_command_at_it()
    {
        var script = new NewVerbScript(@"C:\Tools\run.exe", "-q", "\"%1\"", Hidden: true, RunAsAdministrator: false);

        Assert.True(Creator().CreateVerb(MenuSection.Files, Verb("Quiet run", script: script), false).IsSuccess);

        var scriptFile = Path.Combine(_scripts, "Quietrun.ps1");
        Assert.True(File.Exists(scriptFile));
        Assert.Contains("param([string]$Target)", File.ReadAllText(scriptFile));
        var command = Value(@"*\shell\Quietrun\command", "");
        Assert.Contains(scriptFile, command);
        Assert.EndsWith("\"%1\"", command);
    }

    [Fact]
    public void Icon_and_flags_are_written_on_the_new_verb()
    {
        var verb = new NewVerb("Admin tool", "tool.exe", null, @"%SystemRoot%\System32\shell32.dll,5",
            new VerbAttributes { Shield = true, Position = VerbPosition.Bottom }, []);

        Assert.True(Creator().CreateVerb(MenuSection.Desktop, verb, false).IsSuccess);

        Assert.Equal(@"%SystemRoot%\System32\shell32.dll,5", Value(@"DesktopBackground\shell\Admintool", "Icon"));
        Assert.True(_sandbox.HasValue(@"DesktopBackground\shell\Admintool", "HasLUAShield"));
        Assert.Equal("Bottom", Value(@"DesktopBackground\shell\Admintool", "Position"));
    }

    [Fact]
    public void A_handler_is_added_once_and_a_repeat_is_refused()
    {
        var clsid = Guid.Parse("c2fbb630-2971-11d1-a18c-00c04fd75d13");

        Assert.True(Creator().CreateHandler(MenuSection.FilesAndFolders, clsid, false).IsSuccess);
        Assert.Equal(clsid.ToString("B"), Value($@"AllFilesystemObjects\shellex\ContextMenuHandlers\{clsid:B}", ""));
        Assert.Equal(ErrorKind.InvalidInput, Creator().CreateHandler(MenuSection.FilesAndFolders, clsid, false).Error!.Kind);
    }

    [Fact]
    public void Pages_without_a_registry_class_cannot_take_new_entries()
    {
        Assert.Equal(ErrorKind.NotSupported, Creator().CreateVerb(MenuSection.NewMenu, Verb("X", "x.exe"), false).Error!.Kind);
        Assert.Equal(ErrorKind.NotSupported, Creator().CreateHandler(MenuSection.WinX, Guid.NewGuid(), false).Error!.Kind);
    }

    [Fact]
    public void ClsidExists_looks_at_the_merged_root()
    {
        var clsid = Guid.NewGuid();
        Assert.False(Creator().ClsidExists(clsid));

        _sandbox.Create($@"CLSID\{clsid:B}");

        Assert.True(Creator().ClsidExists(clsid));
    }
}
