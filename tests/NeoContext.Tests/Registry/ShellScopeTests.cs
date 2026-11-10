using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>Every home page (section) maps to its registry class keys, and a section may have several.</summary>
public sealed class ShellScopeTests : IDisposable
{
    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public void Every_section_has_at_least_one_key_and_no_key_is_shared()
    {
        Assert.All(ShellScopes.Keys.Values, keys => Assert.NotEmpty(keys));

        var all = ShellScopes.Keys.Values.SelectMany(k => k).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Folder_and_Directory_are_separate_sections()
    {
        _sandbox.Create(@"Folder\shell\OnlyFolder");
        _sandbox.Create(@"Directory\shell\OnlyDirectory");
        var provider = new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        var entries = provider.Load().Value;

        Assert.Equal(MenuSection.Folder, Assert.Single(entries, e => e.Key == "OnlyFolder").Section);
        Assert.Equal(MenuSection.Directory, Assert.Single(entries, e => e.Key == "OnlyDirectory").Section);
    }

    [Fact]
    public void Library_collects_entries_from_all_three_of_its_keys()
    {
        _sandbox.Create(@"LibraryFolder\shell\A");
        _sandbox.Create(@"LibraryFolder\Background\shell\B");
        _sandbox.Create(@"UserLibraryFolder\shell\C");
        var provider = new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        var library = provider.Load().Value.Where(e => e.Section == MenuSection.Library).Select(e => e.Key).Order().ToList();

        Assert.Equal(["A", "B", "C"], library);
    }

    [Fact]
    public void This_PC_and_Recycle_Bin_read_their_CLSID_keys()
    {
        _sandbox.Create(@"CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\shell\Pc");
        _sandbox.Create(@"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shell\Bin");
        var provider = new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance);

        var entries = provider.Load().Value;

        Assert.Equal(MenuSection.Computer, Assert.Single(entries, e => e.Key == "Pc").Section);
        Assert.Equal(MenuSection.RecycleBin, Assert.Single(entries, e => e.Key == "Bin").Section);
    }
}
