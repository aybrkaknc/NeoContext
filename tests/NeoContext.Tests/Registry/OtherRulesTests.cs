using Microsoft.Win32;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>Drag and drop, public commands, blocked GUIDs and the custom registry path, in a sandbox key.</summary>
public sealed class OtherRulesTests : IDisposable
{
    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private string FullPath(string relative) => $@"HKEY_CURRENT_USER\{_sandbox.Root.SubKey}\{relative}";

    private string? Value(string relative, string name)
    {
        using var key = RegistryStore.OpenRead(_sandbox.Root / relative);
        return key?.GetValue(name)?.ToString();
    }

    private ShellExtensionProvider Handlers(string? customPath = null) =>
        new(_sandbox.Classes, NullIndirectStringResolver.Instance, customPath: () => customPath);

    private ShellVerbProvider Verbs(RegistryPath? commandStore = null, string? customPath = null) =>
        new(_sandbox.Classes, NullIndirectStringResolver.Instance, commandStore, () => customPath);

    [Fact]
    public void Drag_drop_handlers_are_listed_and_move_between_the_active_and_the_disabled_key()
    {
        var clsid = Guid.NewGuid().ToString("B");
        _sandbox.Create(@"Folder\shellex\DragDropHandlers\Copy To", ("", clsid));
        var provider = Handlers();

        var entry = Assert.Single(provider.Load().Value, e => e.Section == MenuSection.DragDrop);
        Assert.Equal(EntryState.Enabled, entry.State);

        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);
        Assert.False(_sandbox.Exists(@"Folder\shellex\DragDropHandlers\Copy To"));
        Assert.True(_sandbox.Exists(@"Folder\shellex\-DragDropHandlers\Copy To"));

        var disabled = Assert.Single(provider.Load().Value, e => e.Section == MenuSection.DragDrop);
        Assert.Equal(EntryState.Disabled, disabled.State);
        Assert.True(provider.SetState(disabled, EntryState.Enabled).IsSuccess);
        Assert.True(_sandbox.Exists(@"Folder\shellex\DragDropHandlers\Copy To"));
    }

    [Fact]
    public void Context_menu_handlers_still_use_their_own_key_pair()
    {
        var clsid = Guid.NewGuid().ToString("B");
        _sandbox.Create(@"*\shellex\ContextMenuHandlers\Menu", ("", clsid));
        var provider = Handlers();
        var entry = Assert.Single(provider.Load().Value, e => e.Section == MenuSection.Files);

        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);

        Assert.True(_sandbox.Exists(@"*\shellex\-ContextMenuHandlers\Menu"));
        Assert.DoesNotContain(provider.Load().Value, e => e.Section == MenuSection.DragDrop);
    }

    [Fact]
    public void The_command_pool_is_listed_without_a_delete_option()
    {
        _sandbox.Create(@"CommandStore\shell\Windows.Test", ("MUIVerb", "Test command"));
        var provider = Verbs(commandStore: _sandbox.Root / @"CommandStore\shell");

        var entry = Assert.Single(provider.Load().Value, e => e.Section == MenuSection.CommandStore);

        Assert.Equal("Test command", entry.DisplayName);
        Assert.True(entry.Can(EntryCapabilities.Toggle));
        Assert.True(entry.Can(EntryCapabilities.Edit));
        Assert.False(entry.Can(EntryCapabilities.Delete));
        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);
        Assert.True(_sandbox.HasValue(@"CommandStore\shell\Windows.Test", "LegacyDisable"));
    }

    [Fact]
    public void The_custom_path_lists_verbs_and_handlers_under_the_chosen_key()
    {
        var clsid = Guid.NewGuid().ToString("B");
        _sandbox.Create(@"Custom\shell\MyVerb", ("MUIVerb", "My verb"));
        _sandbox.Create(@"Custom\shellex\ContextMenuHandlers\MyHandler", ("", clsid));
        _sandbox.Create(@"Custom\shellex\-ContextMenuHandlers\OffHandler", ("", Guid.NewGuid().ToString("B")));
        var path = FullPath("Custom");

        var verbs = Verbs(customPath: path).Load().Value.Where(e => e.Section == MenuSection.CustomPath).ToList();
        var handlers = Handlers(path).Load().Value.Where(e => e.Section == MenuSection.CustomPath).ToList();

        Assert.Equal("My verb", Assert.Single(verbs).DisplayName);
        Assert.Equal(ClassesRoots.CurrentUserLabel, verbs[0].Detail);
        Assert.Equal(2, handlers.Count);
        Assert.Equal(EntryState.Disabled, handlers.Single(h => h.DisplayName.Contains("MyHandler") is false).State);
    }

    [Fact]
    public void Without_a_custom_path_or_with_a_bad_one_nothing_extra_is_listed()
    {
        _sandbox.Create(@"Custom\shell\MyVerb");

        Assert.DoesNotContain(Verbs(customPath: "").Load().Value, e => e.Section == MenuSection.CustomPath);
        Assert.DoesNotContain(Verbs(customPath: "not a registry path").Load().Value, e => e.Section == MenuSection.CustomPath);
        Assert.DoesNotContain(Handlers("   ").Load().Value, e => e.Section == MenuSection.CustomPath);
    }

    private DropEffectProvider Drop() => new(_sandbox.Classes, key => key);

    [Fact]
    public void The_drop_effect_choices_start_on_default_and_writing_one_turns_the_others_off()
    {
        var provider = Drop();
        var entries = provider.Load().Value;

        Assert.Equal(["Default"], entries.Where(e => e.State == EntryState.Enabled).Select(e => e.Key));

        var copy = entries.Single(e => e.Key == "Copy");
        Assert.True(provider.SetState(copy, EntryState.Enabled).IsSuccess);

        foreach (var className in new[] { "*", "AllFilesystemObjects", "Folder", "Directory" })
        {
            Assert.Equal("1", Value(className, "DefaultDropEffect"));
        }

        Assert.Equal(["Copy"], provider.Load().Value.Where(e => e.State == EntryState.Enabled).Select(e => e.Key));

        Assert.True(provider.SetState(provider.Load().Value.Single(e => e.Key == "Link"), EntryState.Enabled).IsSuccess);
        Assert.Equal("4", Value("Folder", "DefaultDropEffect"));
        Assert.Equal(["Link"], provider.Load().Value.Where(e => e.State == EntryState.Enabled).Select(e => e.Key));
    }

    [Fact]
    public void Switching_the_active_choice_off_goes_back_to_the_default_and_other_switches_do_nothing()
    {
        var provider = Drop();
        Assert.True(provider.SetState(provider.Load().Value.Single(e => e.Key == "Move"), EntryState.Enabled).IsSuccess);

        Assert.True(provider.SetState(provider.Load().Value.Single(e => e.Key == "Copy"), EntryState.Disabled).IsSuccess);
        Assert.Equal("2", Value("*", "DefaultDropEffect"));

        Assert.True(provider.SetState(provider.Load().Value.Single(e => e.Key == "Move"), EntryState.Disabled).IsSuccess);
        Assert.Null(Value("*", "DefaultDropEffect"));
        Assert.Null(Value("Directory", "DefaultDropEffect"));
        Assert.Equal(["Default"], provider.Load().Value.Where(e => e.State == EntryState.Enabled).Select(e => e.Key));

        Assert.True(provider.SetState(provider.Load().Value.Single(e => e.Key == "Default"), EntryState.Disabled).IsSuccess);
        Assert.Equal(ErrorKind.NotFound, provider.SetState(provider.Load().Value[0] with { Key = "Bogus" }, EntryState.Enabled).Error!.Kind);
    }

    private (BlockedShellExtensions Blocked, RegistryPath User, RegistryPath Machine) BlockedLists()
    {
        var user = _sandbox.Root / "BlockedUser";
        var machine = _sandbox.Root / "BlockedMachine";
        return (new BlockedShellExtensions(user, machine), user, machine);
    }

    [Fact]
    public void Blocked_extensions_from_both_lists_are_shown_with_a_name_and_removed_one_list_at_a_time()
    {
        var (blocked, _, machine) = BlockedLists();
        var known = Guid.NewGuid();
        var machineOnly = Guid.NewGuid();
        Assert.True(blocked.Block(known).IsSuccess);
        _sandbox.Create(@"BlockedMachine", ($"{machineOnly:B}", ""));
        var catalog = GuidInfoCatalog.Parse($"[{known}]\nText = Dictionary name\n", "en-US");
        var provider = new GuidBlockedProvider(blocked, _sandbox.Classes, NullIndirectStringResolver.Instance, catalog);

        var entries = provider.Load().Value;

        Assert.Equal(2, entries.Count);
        var user = entries.Single(e => e.Detail == ClassesRoots.CurrentUserLabel);
        var all = entries.Single(e => e.Detail == ClassesRoots.AllUsersLabel);
        Assert.Equal("Dictionary name", user.DisplayName);
        Assert.Equal(machineOnly.ToString("B"), all.DisplayName);
        Assert.Equal(EntryCapabilities.Delete, user.Capabilities);

        Assert.True(provider.Delete(all).IsSuccess);
        Assert.Equal(known.ToString("B"), Assert.Single(provider.Load().Value).Key);
        Assert.True(RegistryStore.Exists(machine)); // only the value is removed, never the list key
    }

    [Fact]
    public void Blocked_entries_cannot_be_toggled_or_renamed()
    {
        var (blocked, _, _) = BlockedLists();
        blocked.Block(Guid.NewGuid());
        var provider = new GuidBlockedProvider(blocked, _sandbox.Classes, NullIndirectStringResolver.Instance);
        var entry = Assert.Single(provider.Load().Value);

        Assert.Equal(ErrorKind.NotSupported, provider.SetState(entry, EntryState.Enabled).Error!.Kind);
        Assert.Equal(ErrorKind.NotSupported, provider.Rename(entry, "x").Error!.Kind);
        Assert.Equal(ErrorKind.InvalidInput, provider.Delete(entry with { Key = "nope" }).Error!.Kind);
    }

    [Fact]
    public void The_custom_registry_path_is_kept_in_the_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"neocontext-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new NeoContext.Core.Settings.JsonSettingsStore(path);
            store.Save(store.Load() with { CustomRegistryPath = @"HKEY_CLASSES_ROOT\Directory" });

            Assert.Equal(@"HKEY_CLASSES_ROOT\Directory", store.Load().CustomRegistryPath);
            Assert.Equal("", NeoContext.Core.Settings.AppSettings.Default.CustomRegistryPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Registry_hive_names_in_the_sandbox_paths_parse()
    {
        Assert.True(RegistryPath.TryParse(FullPath("Custom"), out var parsed));
        Assert.Equal(RegistryHive.CurrentUser, parsed.Hive);
    }
}
