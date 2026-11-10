using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;
using NeoContext.Core.Services;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>Editing a verb: command, icon, position and flags are written, and removed again when switched off.</summary>
public sealed class EntryEditTests : IDisposable
{
    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    private ShellVerbProvider Provider() => new(_sandbox.Classes, NullIndirectStringResolver.Instance);

    private ContextMenuEntry Load(string key)
    {
        var result = Provider().Load();
        Assert.True(result.IsSuccess, result.Error?.Message);
        return Assert.Single(result.Value, e => e.Key == key);
    }

    [Fact]
    public void Edit_writes_command_icon_position_and_flags()
    {
        _sandbox.Create(@"*\shell\Scan");
        _sandbox.Create(@"*\shell\Scan\command", ("", "old.exe \"%1\""));
        var entry = Load("Scan");

        var edit = new EntryEdit("new.exe \"%1\"", @"%SystemRoot%\System32\shell32.dll,3", new VerbAttributes
        {
            Position = VerbPosition.Top,
            Extended = true,
            Shield = true,
            NeverDefault = true,
        });
        Assert.True(Provider().Edit(entry, edit).IsSuccess);

        var reloaded = Load("Scan");
        Assert.Equal("new.exe \"%1\"", reloaded.Command);
        Assert.Equal(@"%SystemRoot%\System32\shell32.dll,3", reloaded.IconLocation);
        Assert.Equal(VerbPosition.Top, reloaded.Attributes!.Position);
        Assert.True(reloaded.Attributes.Extended);
        Assert.True(reloaded.Attributes.Shield);
        Assert.True(reloaded.Attributes.NeverDefault);
        Assert.False(reloaded.Attributes.OnlyInBrowserWindow);
    }

    [Fact]
    public void Switching_everything_off_removes_the_values_again()
    {
        _sandbox.Create(@"*\shell\Scan", ("Icon", "x.exe,0"), ("Position", "Bottom"), ("Extended", ""), ("HasLUAShield", ""));
        var entry = Load("Scan");

        Assert.True(Provider().Edit(entry, new EntryEdit(null, "", new VerbAttributes())).IsSuccess);

        Assert.False(_sandbox.HasValue(@"*\shell\Scan", "Icon"));
        Assert.False(_sandbox.HasValue(@"*\shell\Scan", "Position"));
        Assert.False(_sandbox.HasValue(@"*\shell\Scan", "Extended"));
        Assert.False(_sandbox.HasValue(@"*\shell\Scan", "HasLUAShield"));
    }

    [Fact]
    public void A_null_command_leaves_the_command_alone_and_does_not_create_one()
    {
        _sandbox.Create(@"*\shell\Menu", ("MUIVerb", "Submenu"), ("SubCommands", "a;b"));
        var entry = Load("Menu");

        Assert.True(Provider().Edit(entry, new EntryEdit(null, null, new VerbAttributes { NoWorkingDirectory = true })).IsSuccess);

        Assert.False(_sandbox.Exists(@"*\shell\Menu\command"));
        Assert.True(_sandbox.HasValue(@"*\shell\Menu", "NoWorkingDirectory"));
    }

    [Fact]
    public void A_flag_that_already_holds_a_value_keeps_it()
    {
        _sandbox.Create(@"*\shell\Scan", ("NeverDefault", "keep"));
        var entry = Load("Scan");

        Assert.True(Provider().Edit(entry, new EntryEdit(null, null, new VerbAttributes { NeverDefault = true })).IsSuccess);

        using var key = RegistryStore.OpenRead(_sandbox.Root / @"*\shell\Scan");
        Assert.Equal("keep", key!.GetValue("NeverDefault"));
    }

    [Fact]
    public void The_service_refuses_to_edit_protected_entries_and_empty_commands()
    {
        _sandbox.Create(@"*\shell\open");
        _sandbox.Create(@"*\shell\open\command", ("", "app.exe"));
        _sandbox.Create(@"*\shell\Scan");
        _sandbox.Create(@"*\shell\Scan\command", ("", "scan.exe"));
        var service = new ContextMenuService([Provider()], new ProtectionPolicy(enabled: true));
        service.Refresh();

        var open = Assert.Single(service.Entries, e => e.Key == "open");
        var scan = Assert.Single(service.Entries, e => e.Key == "Scan");

        Assert.Equal(ErrorKind.Protected, service.Edit(open, new EntryEdit("x.exe", null, new VerbAttributes())).Error!.Kind);
        Assert.Equal(ErrorKind.InvalidInput, service.Edit(scan, new EntryEdit("  ", null, new VerbAttributes())).Error!.Kind);
        Assert.True(service.Edit(scan, new EntryEdit("scan2.exe", null, new VerbAttributes())).IsSuccess);
    }
}
