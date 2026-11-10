using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>The "Open with" list: apps under Applications\&lt;exe&gt;, hiding, renaming, deleting and registering.</summary>
public sealed class OpenWithTests : IDisposable
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

    private OpenWithProvider Provider() => new(_sandbox.Classes, NullIndirectStringResolver.Instance);

    private ContextMenuEntry Load(string app)
    {
        var result = Provider().Load();
        Assert.True(result.IsSuccess, result.Error?.Message);
        return Assert.Single(result.Value, e => e.Key == app);
    }

    private string Exe(string name)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Only_apps_with_a_dot_and_a_usable_command_are_listed()
    {
        _sandbox.Create(@"Applications\notepad2.exe\shell\open\command", ("", "\"C:\\n\\notepad2.exe\" \"%1\""));
        _sandbox.Create(@"Applications\NoDot\shell\open\command", ("", "x.exe"));
        _sandbox.Create(@"Applications\empty.exe\shell");
        _sandbox.Create(@"Applications\never.exe\shell\open", ("NeverDefault", ""));
        _sandbox.Create(@"Applications\never.exe\shell\open\command", ("", "never.exe"));

        var entries = Provider().Load().Value;

        var entry = Assert.Single(entries);
        Assert.Equal("notepad2.exe", entry.Key);
        Assert.Equal(MenuSection.OpenWith, entry.Section);
        Assert.Equal("\"C:\\n\\notepad2.exe\" \"%1\"", entry.Command);
    }

    [Fact]
    public void The_name_is_FriendlyAppName_when_there_is_one_and_the_exe_name_otherwise()
    {
        _sandbox.Create(@"Applications\a.exe\shell\open\command", ("", "a.exe"));
        _sandbox.Create(@"Applications\b.exe", ("FriendlyAppName", "Better B"));
        _sandbox.Create(@"Applications\b.exe\shell\open\command", ("", "b.exe"));

        Assert.Equal("a.exe", Load("a.exe").DisplayName);
        Assert.Equal("Better B", Load("b.exe").DisplayName);
    }

    [Fact]
    public void Hiding_sets_NoOpenWith_and_showing_removes_it()
    {
        _sandbox.Create(@"Applications\a.exe\shell\open\command", ("", "a.exe"));
        var provider = Provider();

        Assert.True(provider.SetState(Load("a.exe"), EntryState.Disabled).IsSuccess);
        Assert.True(_sandbox.HasValue(@"Applications\a.exe", "NoOpenWith"));
        Assert.Equal(EntryState.Disabled, Load("a.exe").State);

        Assert.True(provider.SetState(Load("a.exe"), EntryState.Enabled).IsSuccess);
        Assert.False(_sandbox.HasValue(@"Applications\a.exe", "NoOpenWith"));
    }

    [Fact]
    public void Renaming_writes_FriendlyAppName_and_deleting_removes_the_app_key()
    {
        _sandbox.Create(@"Applications\a.exe\shell\open\command", ("", "a.exe"));
        var provider = Provider();

        Assert.True(provider.Rename(Load("a.exe"), "My A").IsSuccess);
        Assert.Equal("My A", Load("a.exe").DisplayName);

        Assert.True(provider.Delete(Load("a.exe")).IsSuccess);
        Assert.False(_sandbox.Exists(@"Applications\a.exe"));
    }

    [Fact]
    public void A_registered_program_gets_an_open_verb_and_an_optional_name()
    {
        var exe = Exe("tool.exe");

        Assert.True(Provider().CreateApp(exe, "My tool", allUsers: false).IsSuccess);

        var entry = Load("tool.exe");
        Assert.Equal("My tool", entry.DisplayName);
        Assert.Equal($"\"{exe}\" \"%1\"", entry.Command);
    }

    [Fact]
    public void Registering_refuses_duplicates_missing_files_and_names_without_a_dot()
    {
        var exe = Exe("tool.exe");
        Assert.True(Provider().CreateApp(exe, null, false).IsSuccess);

        Assert.Equal(ErrorKind.InvalidInput, Provider().CreateApp(exe, null, false).Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, Provider().CreateApp(Path.Combine(_folder, "missing.exe"), null, false).Error!.Kind);
        Assert.Equal(ErrorKind.InvalidInput, Provider().CreateApp(Exe("nodot"), null, false).Error!.Kind);
        Assert.False(_sandbox.Exists(@"Applications\nodot"));
    }
}
