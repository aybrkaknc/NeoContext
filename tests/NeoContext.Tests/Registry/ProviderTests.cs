using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>Add/disable/enable/rename/delete cycles against a sandbox key in HKCU.</summary>
public sealed class ProviderTests : IDisposable
{
    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private static ContextMenuEntry Single(Result<IReadOnlyList<ContextMenuEntry>> result, string key)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return Assert.Single(result.Value, e => e.Key == key);
    }

    [Fact]
    public void Shell_verb_is_listed_with_name_command_and_section()
    {
        _sandbox.Create(@"Directory\Background\shell\Terminal", ("MUIVerb", "Open &Terminal here"), ("Extended", ""));
        _sandbox.Create(@"Directory\Background\shell\Terminal\command", ("", "wt.exe -d \"%V\""));
        var provider = new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        var entry = Single(provider.Load(), "Terminal");

        Assert.Equal(MenuSection.FolderBackground, entry.Section);
        Assert.Equal("Open Terminal here", entry.DisplayName);
        Assert.Equal("wt.exe -d \"%V\"", entry.Command);
        Assert.True(entry.ExtendedOnly);
        Assert.Equal(EntryState.Enabled, entry.State);
    }

    [Fact]
    public void Shell_verb_disable_enable_cycle_keeps_CommandFlags()
    {
        _sandbox.Create(@"*\shell\Scan", ("CommandFlags", "32"));
        var provider = new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        Assert.True(provider.SetState(Single(provider.Load(), "Scan"), EntryState.Disabled).IsSuccess);
        Assert.Equal(EntryState.Disabled, Single(provider.Load(), "Scan").State);
        Assert.True(_sandbox.HasValue(@"*\shell\Scan", "LegacyDisable"));

        Assert.True(provider.SetState(Single(provider.Load(), "Scan"), EntryState.Enabled).IsSuccess);
        Assert.Equal(EntryState.Enabled, Single(provider.Load(), "Scan").State);
        Assert.True(_sandbox.HasValue(@"*\shell\Scan", "CommandFlags"));
    }

    [Fact]
    public void Shell_verb_with_ProgrammaticAccessOnly_counts_as_disabled_and_enable_clears_it()
    {
        _sandbox.Create(@"Drive\shell\Hidden", ("ProgrammaticAccessOnly", ""));
        var provider = new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        var entry = Single(provider.Load(), "Hidden");
        Assert.Equal(EntryState.Disabled, entry.State);

        provider.SetState(entry, EntryState.Enabled);
        Assert.False(_sandbox.HasValue(@"Drive\shell\Hidden", "ProgrammaticAccessOnly"));
    }

    [Fact]
    public void Shell_verb_rename_and_delete()
    {
        _sandbox.Create(@"*\shell\Old");
        var provider = new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        provider.Rename(Single(provider.Load(), "Old"), "New name");
        var renamed = Single(provider.Load(), "Old");
        Assert.Equal("New name", renamed.DisplayName);

        Assert.True(provider.Delete(renamed).IsSuccess);
        Assert.False(_sandbox.Exists(@"*\shell\Old"));
    }

    [Fact]
    public void Shell_extension_moves_between_active_and_disabled_keys()
    {
        var clsid = Guid.NewGuid().ToString("B");
        _sandbox.Create(@"*\shellex\ContextMenuHandlers\MyHandler", ("", clsid));
        var provider = new ShellExtensionProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        var entry = Single(provider.Load(), clsid);
        Assert.Equal("MyHandler", entry.DisplayName);

        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);
        Assert.True(_sandbox.Exists(@"*\shellex\-ContextMenuHandlers\MyHandler"));
        Assert.False(_sandbox.Exists(@"*\shellex\ContextMenuHandlers\MyHandler"));
        Assert.Equal(EntryState.Disabled, Single(provider.Load(), clsid).State);

        Assert.True(provider.SetState(Single(provider.Load(), clsid), EntryState.Enabled).IsSuccess);
        Assert.True(_sandbox.Exists(@"*\shellex\ContextMenuHandlers\MyHandler"));
    }

    [Fact]
    public void Shell_extension_named_by_clsid_uses_the_com_class_name()
    {
        var clsid = Guid.NewGuid().ToString("B");
        _sandbox.Create($@"Directory\shellex\ContextMenuHandlers\{clsid}");
        _sandbox.Create($@"CLSID\{clsid}", ("", "Contoso Archiver"));
        var provider = new ShellExtensionProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        Assert.Equal("Contoso Archiver", Single(provider.Load(), clsid).DisplayName);
    }

    [Fact]
    public void Shell_extension_move_fails_cleanly_when_target_exists()
    {
        var clsid = Guid.NewGuid().ToString("B");
        _sandbox.Create(@"*\shellex\ContextMenuHandlers\Dup", ("", clsid));
        _sandbox.Create(@"*\shellex\-ContextMenuHandlers\Dup", ("", clsid));
        var provider = new ShellExtensionProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        var active = provider.Load().Value.Single(e => e.State == EntryState.Enabled);
        var result = provider.SetState(active, EntryState.Disabled);

        Assert.Equal(ErrorKind.InvalidInput, result.Error?.Kind);
        Assert.True(_sandbox.Exists(@"*\shellex\ContextMenuHandlers\Dup"));
    }

    [Fact]
    public void ShellNew_item_under_progid_toggles_and_invalidates_explorer_cache()
    {
        _sandbox.Create(".neo", ("", "NeoFile"));
        _sandbox.Create(@".neo\NeoFile\ShellNew", ("NullFile", ""));
        _sandbox.Create("NeoFile", ("", "Neo document"));
        var cache = _sandbox.Create("ExplorerCache", ("Classes", ".neo"));
        var provider = new ShellNewProvider(_sandbox.Classes, NullIndirectStringResolver.Instance, cache);

        var entry = Single(provider.Load(), ".neo");
        Assert.Equal("Neo document", entry.DisplayName);
        Assert.Equal(MenuSection.NewMenu, entry.Section);

        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);
        Assert.True(_sandbox.Exists(@".neo\NeoFile\-ShellNew"));
        Assert.False(_sandbox.HasValue("ExplorerCache", "Classes"));
        Assert.Equal(EntryState.Disabled, Single(provider.Load(), ".neo").State);
    }

    [Fact]
    public void ShellNew_key_without_template_value_is_ignored()
    {
        _sandbox.Create(@".empty\ShellNew", ("ItemName", "x"));
        var provider = new ShellNewProvider(_sandbox.Classes, NullIndirectStringResolver.Instance, _sandbox.Root / "cache");

        Assert.DoesNotContain(provider.Load().Value, e => e.Key == ".empty");
    }

    [Fact]
    public void Move_copies_values_of_every_kind_and_subkeys()
    {
        var source = _sandbox.Create(@"src\child", ("s", "text"));
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey((_sandbox.Root / "src").SubKey, writable: true)!)
        {
            key.SetValue("n", 42, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("e", "%TEMP%", Microsoft.Win32.RegistryValueKind.ExpandString);
        }

        Assert.True(RegistryStore.Move(_sandbox.Root / "src", _sandbox.Root / "dst").IsSuccess);

        using var moved = RegistryStore.OpenRead(_sandbox.Root / "dst")!;
        Assert.Equal(42, moved.GetValue("n"));
        Assert.Equal("%TEMP%", moved.GetValue("e", null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.True(_sandbox.HasValue(@"dst\child", "s"));
        Assert.False(_sandbox.Exists("src"));
        _ = source;
    }
}
