using Microsoft.Win32;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>Switching ready-made items on and off in a sandbox key, with scripts in a temporary folder.</summary>
public sealed class EnhanceMenuStoreTests : IDisposable
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

    private EnhanceMenuStore Store() => new(_sandbox.Classes, _scripts);

    private static EnhanceGroup Group(params EnhanceItem[] items) => new("Group", null, @"HKEY_CLASSES_ROOT\Directory\Background", items);

    private static RegNode Leaf(string name, params (string Name, string Data)[] values) =>
        new(name, null, [.. values.Select(v => new RegValue(v.Name, RegValueKind.Text, v.Data))], null, []);

    private static RegNode CommandNode(CommandSpec spec) => new("command", null, [], spec, []);

    private static EnhanceItem Verb(string key, RegNode node) =>
        new(EnhanceKind.Verb, key, node.Values.FirstOrDefault(v => v.Name == "MUIVerb")?.Data ?? key, null, null, node, null);

    private static CommandSpec Plain(string program, string arguments = "", ShellExecuteSpec? execute = null, ScriptFile? script = null, string suffix = "") =>
        new(null, program, null, arguments, script, "", suffix, execute);

    private string? Value(string relativePath, string name)
    {
        using var key = RegistryStore.OpenRead(_sandbox.Root / relativePath);
        return key?.GetValue(name)?.ToString();
    }

    [Fact]
    public void A_verb_is_written_with_values_and_a_command_and_removed_again()
    {
        var node = new RegNode("Tool", null, [new RegValue("MUIVerb", RegValueKind.Text, "My tool"), new RegValue("Flag", RegValueKind.DWord, "4")], null,
            [CommandNode(new CommandSpec(@"cmd.exe /c echo ""%V""", "", null, "", null, "", "", null))]);
        var group = Group(Verb("Tool", node));
        var item = group.Items[0];

        Assert.False(Store().IsEnabled(group, item));
        Assert.True(Store().Enable(group, item).IsSuccess);

        Assert.True(Store().IsEnabled(group, item));
        Assert.Equal("My tool", Value(@"Directory\Background\shell\Tool", "MUIVerb"));
        Assert.Equal(@"cmd.exe /c echo ""%V""", Value(@"Directory\Background\shell\Tool\command", ""));
        using (var key = RegistryStore.OpenRead(_sandbox.Root / @"Directory\Background\shell\Tool"))
        {
            Assert.Equal(RegistryValueKind.DWord, key!.GetValueKind("Flag"));
            Assert.Equal(4, key.GetValue("Flag"));
        }

        Assert.True(Store().Disable(group, item).IsSuccess);
        Assert.False(Store().IsEnabled(group, item));
        Assert.False(_sandbox.Exists(@"Directory\Background\shell\Tool"));
    }

    [Fact]
    public void A_command_with_a_script_file_writes_the_file_and_names_it_in_the_command()
    {
        var script = new ScriptFile("Do.vbs", "Dim wsh\r\nSet wsh = Nothing");
        var spec = new CommandSpec(null, "wscript.exe", null, "", script, "", " \"%1\"", null);
        var group = Group(Verb("Do", new RegNode("Do", null, [], null, [CommandNode(spec)])));

        Assert.True(Store().Enable(group, group.Items[0]).IsSuccess);

        var path = Path.Combine(_scripts, "Do.vbs");
        Assert.Equal($"wscript.exe \"{path}\" \"%1\"", Value(@"Directory\Background\shell\Do\command", ""));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xFF, 0xFE], bytes[..2]); // VBScript is written as UTF-16 with a byte order mark
        Assert.Contains("Set wsh = Nothing", File.ReadAllText(path));
    }

    [Fact]
    public void An_elevated_hidden_command_runs_through_a_script_that_receives_the_item()
    {
        var spec = Plain("takeown.exe", "/f \"%1\" /a", new ShellExecuteSpec("runas", 0));
        var group = Group(Verb("Own", new RegNode("Own", null, [], null, [CommandNode(spec)])));

        Assert.True(Store().Enable(group, group.Items[0]).IsSuccess);

        var command = Value(@"Directory\Background\shell\Own\command", "")!;
        var scriptFile = Directory.EnumerateFiles(_scripts, "*.ps1").Single();
        Assert.StartsWith("powershell.exe", command);
        Assert.EndsWith($"-File \"{scriptFile}\" \"%1\"", command);
        var content = File.ReadAllText(scriptFile);
        Assert.Contains("$start.Verb = 'RunAs'", content);
        Assert.Contains("$start.WindowStyle = 'Hidden'", content);
        Assert.Contains(".Replace('%1', $Target)", content);
        Assert.DoesNotContain("takeown.exe /f", content); // the item is data, never part of the script text
    }

    [Fact]
    public void The_main_command_is_shown_without_writing_any_file()
    {
        var spec = Plain("takeown.exe", "/f \"%1\"", new ShellExecuteSpec("runas", null));
        var group = Group(Verb("Own", new RegNode("Own", null, [], null, [CommandNode(spec)])));

        var command = Store().MainCommand(group, group.Items[0]);

        Assert.Contains("powershell.exe", command);
        Assert.False(Directory.Exists(_scripts));
    }

    [Fact]
    public void A_handler_is_found_by_its_clsid_even_under_another_key_name()
    {
        var clsid = Guid.Parse("c2fbb630-2971-11d1-a18c-00c04fd75d13");
        var item = new EnhanceItem(EnhanceKind.Handler, "CopyTo", "Copy to", null, null, null, clsid);
        var group = Group(item);

        Assert.False(Store().IsEnabled(group, item));
        _sandbox.Create(@"Directory\Background\shellex\ContextMenuHandlers\OtherName", ("", clsid.ToString("B")));
        Assert.True(Store().IsEnabled(group, item));

        Assert.True(Store().Disable(group, item).IsSuccess);
        Assert.False(_sandbox.Exists(@"Directory\Background\shellex\ContextMenuHandlers\OtherName"));
        Assert.True(Store().Enable(group, item).IsSuccess);
        Assert.Equal(clsid.ToString("B"), Value(@"Directory\Background\shellex\ContextMenuHandlers\CopyTo", ""));
    }

    [Fact]
    public void A_group_outside_the_classes_root_is_refused()
    {
        var group = new EnhanceGroup("G", null, @"HKEY_LOCAL_MACHINE\SOFTWARE\X", []);
        var item = Verb("A", Leaf("A", ("MUIVerb", "a")));

        Assert.Equal(ErrorKind.NotSupported, Store().Enable(group, item).Error!.Kind);
        Assert.False(EnhanceMenuStore.IsSupported(group));
    }

    [Fact]
    public void The_provider_lists_items_with_state_and_toggles_them()
    {
        var group = Group(Verb("Tool", new RegNode("Tool", null, [new RegValue("MUIVerb", RegValueKind.Text, "My tool")], null, [CommandNode(Plain("tool.exe"))])));
        var provider = new EnhanceMenuProvider(Store(), NullIndirectStringResolver.Instance, () => [group]);

        var entry = Assert.Single(provider.Load().Value);
        Assert.Equal("My tool", entry.DisplayName);
        Assert.Equal("Group", entry.Detail);
        Assert.Equal(MenuSection.Enhance, entry.Section);
        Assert.Equal(EntryState.Disabled, entry.State);
        Assert.Equal("tool.exe", entry.Command);

        Assert.True(provider.SetState(entry, EntryState.Enabled).IsSuccess);
        Assert.Equal(EntryState.Enabled, Assert.Single(provider.Load().Value).State);

        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);
        Assert.Equal(EntryState.Disabled, Assert.Single(provider.Load().Value).State);
        Assert.Equal(ErrorKind.NotSupported, provider.Rename(entry, "x").Error!.Kind);
    }
}
