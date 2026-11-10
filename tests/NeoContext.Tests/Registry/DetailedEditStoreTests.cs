using NeoContext.Core.Abstractions;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Models;
using NeoContext.Core.Rules;
using NeoContext.Core.Services;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>Detailed editing rules against values in a sandbox key.</summary>
public sealed class DetailedEditStoreTests : IDisposable
{
    private readonly RegistrySandbox _sandbox = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"NeoContext.Tests.{Guid.NewGuid():N}");

    public void Dispose()
    {
        _sandbox.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string Path_(string relative) => $@"HKEY_CURRENT_USER\{_sandbox.Root.SubKey}\{relative}";

    private DetailedRule Rule(string relative, string name, string? on, string? off, RegValueKind kind = RegValueKind.DWord) =>
        new(Path_(relative), name, kind, on, off);

    private static DetailedItem Item(params DetailedRule[] rules) => new("Item", null, false, DetailedKind.Toggle, rules);

    private string? Value(string relative, string name)
    {
        using var key = RegistryStore.OpenRead(_sandbox.Root / relative);
        return key?.GetValue(name)?.ToString();
    }

    private DetailedEditStore Store() => new(_sandbox.Classes);

    [Fact]
    public void With_no_matching_rule_the_setting_counts_as_on()
    {
        var item = Item(Rule("Policies", "NoThing", on: null, off: "1"));

        Assert.True(DetailedEditStore.IsOn(item));
        _sandbox.Create("Policies");
        Assert.True(DetailedEditStore.IsOn(item));
    }

    [Fact]
    public void A_value_equal_to_Off_turns_the_setting_off_and_switching_writes_and_removes_it()
    {
        var item = Item(Rule("Policies", "NoThing", on: null, off: "1"));

        Assert.True(DetailedEditStore.Set(item, on: false).IsSuccess);
        Assert.Equal("1", Value("Policies", "NoThing"));
        Assert.False(DetailedEditStore.IsOn(item));

        Assert.True(DetailedEditStore.Set(item, on: true).IsSuccess);
        Assert.Null(Value("Policies", "NoThing"));
        Assert.True(DetailedEditStore.IsOn(item));
    }

    [Fact]
    public void A_rule_that_wants_On_and_finds_no_value_counts_as_off()
    {
        var item = Item(Rule("Adv", "ShowIt", on: "1", off: null));

        Assert.False(DetailedEditStore.IsOn(item));
        Assert.True(DetailedEditStore.Set(item, on: true).IsSuccess);
        Assert.Equal("1", Value("Adv", "ShowIt"));
        Assert.True(DetailedEditStore.IsOn(item));
        Assert.True(DetailedEditStore.Set(item, on: false).IsSuccess);
        Assert.False(DetailedEditStore.IsOn(item));
    }

    [Fact]
    public void An_earlier_rule_with_no_value_is_skipped_and_the_next_one_decides()
    {
        var item = Item(Rule("Machine", "NoThing", on: null, off: "1"), Rule("User", "NoThing", on: null, off: "1"));
        _sandbox.Create("User");
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{_sandbox.Root.SubKey}\User", writable: true))
        {
            key!.SetValue("NoThing", 1);
        }

        Assert.False(DetailedEditStore.IsOn(item));
    }

    [Fact]
    public void Switching_applies_every_rule_and_text_and_binary_values_work()
    {
        var item = Item(
            Rule("A", "Text", on: "%s", off: null, kind: RegValueKind.Text),
            Rule("B", "Link", on: "00 00 00 00", off: null, kind: RegValueKind.Binary));

        Assert.True(DetailedEditStore.Set(item, on: true).IsSuccess);

        Assert.Equal("%s", Value("A", "Text"));
        using (var key = RegistryStore.OpenRead(_sandbox.Root / "B"))
        {
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, key!.GetValue("Link"));
        }

        Assert.True(DetailedEditStore.IsOn(item));
        Assert.True(DetailedEditStore.Set(item, on: false).IsSuccess);
        Assert.Null(Value("A", "Text"));
        Assert.Null(Value("B", "Link"));
    }

    [Fact]
    public void A_program_counts_as_installed_when_the_file_of_its_clsid_exists()
    {
        var clsid = Guid.NewGuid();
        Directory.CreateDirectory(_folder);
        var dll = Path.Combine(_folder, "ext.dll");
        File.WriteAllText(dll, "x");

        Assert.False(Store().IsInstalled(clsid));
        _sandbox.Create($@"CLSID\{clsid:B}\InprocServer32", ("", dll));

        Assert.True(Store().IsInstalled(clsid));
        Assert.Equal(dll, Store().ClsidFilePath(clsid));
    }

    [Fact]
    public void The_provider_shows_a_group_only_when_its_program_is_installed_and_lists_toggles_only()
    {
        var installed = Guid.NewGuid();
        Directory.CreateDirectory(_folder);
        var dll = Path.Combine(_folder, "ext.dll");
        File.WriteAllText(dll, "x");
        _sandbox.Create($@"CLSID\{installed:B}\InprocServer32", ("", dll));

        var toggle = new DetailedItem("Context menu", "A tip", true, DetailedKind.Toggle, [Rule("P", "NoMenu", null, "1")]);
        var number = new DetailedItem("A limit", null, false, DetailedKind.Number, [Rule("P", "Limit", null, null)]);
        DetailedGroup[] groups =
        [
            new("Installed program", null, [installed], Path_("P"), [toggle, number]),
            new("Missing program", null, [Guid.NewGuid()], Path_("Q"), [toggle]),
            new("Always shown", null, [], Path_("R"), [toggle]),
        ];
        var provider = new DetailedEditProvider(Store(), NullIndirectStringResolver.Instance, () => groups);

        var entries = provider.Load().Value;

        Assert.Equal(["Installed program", "Always shown"], entries.Select(e => e.Detail));
        var entry = entries[0];
        Assert.Equal("Context menu", entry.DisplayName);
        Assert.Equal("A tip", entry.Description);
        Assert.True(entry.RestartExplorer);
        Assert.Equal(MenuSection.Detailed, entry.Section);

        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);
        Assert.Equal("1", Value("P", "NoMenu"));
        Assert.Equal(EntryState.Disabled, provider.Load().Value[0].State);
    }

    [Fact]
    public void An_entry_that_asks_for_an_Explorer_restart_gets_one_whatever_its_provider_says()
    {
        var group = new DetailedGroup("G", null, [], Path_("P"), [new DetailedItem("I", null, true, DetailedKind.Toggle, [Rule("P", "V", null, "1")])]);
        var service = new ContextMenuService([new DetailedEditProvider(Store(), NullIndirectStringResolver.Instance, () => [group])], new ProtectionPolicy(enabled: true));
        service.Refresh();

        Assert.True(service.RequiresExplorerRestart(service.Entries[0]));
        Assert.False(service.RequiresExplorerRestart(service.Entries[0] with { RestartExplorer = false }));
    }

    [Fact]
    public void A_handler_named_by_clsid_gets_its_name_and_icon_from_the_guid_dictionary()
    {
        var clsid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        _sandbox.Create($@"*\shellex\ContextMenuHandlers\{clsid:B}", ("", clsid.ToString("B")));
        var catalog = GuidInfoCatalog.Parse($"[{clsid}]\nText = Dictionary name\nIcon = imageres.dll, -109\n", "en-US");

        var entry = Assert.Single(new ShellExtensionProvider(_sandbox.Classes, NullIndirectStringResolver.Instance, catalog).Load().Value);

        Assert.Equal("Dictionary name", entry.DisplayName);
        Assert.Equal("imageres.dll, -109", entry.IconLocation);
        Assert.Equal(clsid.ToString("B"), Assert.Single(new ShellExtensionProvider(_sandbox.Classes, NullIndirectStringResolver.Instance).Load().Value).DisplayName);
    }
}
