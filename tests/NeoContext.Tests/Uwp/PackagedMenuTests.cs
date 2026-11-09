using System.Xml.Linq;
using NeoContext.Core.Models;
using NeoContext.Registry;
using NeoContext.Tests.Registry;
using NeoContext.Uwp;

namespace NeoContext.Tests.Uwp;

public sealed class PackagedMenuTests : IDisposable
{
    private const string Clsid = "{9F156763-7844-4DC4-B2B1-901F640F5155}";

    private const string Manifest = """
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4"
                 xmlns:desktop5="http://schemas.microsoft.com/appx/manifest/desktop/windows10/5"
                 xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10">
          <Applications><Application Id="App"><Extensions>
            <desktop4:Extension Category="windows.fileExplorerContextMenus">
              <desktop4:FileExplorerContextMenus>
                <desktop5:ItemType Type="Directory">
                  <desktop5:Verb Id="OpenInTerminal" Clsid="9F156763-7844-4DC4-B2B1-901F640F5155" />
                </desktop5:ItemType>
                <desktop5:ItemType Type="Directory\Background">
                  <desktop5:Verb Id="OpenInTerminal" Clsid="9F156763-7844-4DC4-B2B1-901F640F5155" />
                </desktop5:ItemType>
              </desktop4:FileExplorerContextMenus>
            </desktop4:Extension>
            <com:Extension Category="windows.comServer"><com:ComServer><com:SurrogateServer>
              <com:Class Id="9F156763-7844-4DC4-B2B1-901F640F5155" DisplayName="Open in Terminal" />
            </com:SurrogateServer></com:ComServer></com:Extension>
          </Extensions></Application></Applications>
        </Package>
        """;

    private readonly RegistrySandbox _sandbox = new();
    private readonly string _installDir = Directory.CreateTempSubdirectory("neocontext-pkg-").FullName;

    public void Dispose()
    {
        _sandbox.Dispose();
        Directory.Delete(_installDir, recursive: true);
    }

    [Fact]
    public void Manifest_reader_finds_verbs_under_any_namespace_version()
    {
        var verbs = AppxManifestReader.ReadContextMenuVerbs(XDocument.Parse(Manifest));

        Assert.Equal(2, verbs.Count);
        Assert.All(verbs, v => Assert.Equal(Guid.Parse(Clsid), v.Clsid));
        Assert.Equal(["Directory", @"Directory\Background"], verbs.Select(v => v.ItemType));
        Assert.Equal("Open in Terminal", verbs[0].ClassDisplayName);
    }

    [Fact]
    public void Manifest_without_context_menus_gives_nothing()
    {
        Assert.Empty(AppxManifestReader.ReadContextMenuVerbs(XDocument.Parse("<Package />")));
        Assert.Empty(AppxManifestReader.ReadFromInstallFolder(_installDir));
    }

    [Fact]
    public void Provider_groups_by_class_and_toggles_through_the_block_list()
    {
        File.WriteAllText(Path.Combine(_installDir, "AppxManifest.xml"), Manifest);
        var blocked = new BlockedShellExtensions(_sandbox.Root / "UserBlocked", _sandbox.Root / "MachineBlocked");
        var provider = new PackagedMenuProvider(new FakePackages(new InstalledPackage("Contoso.Terminal_8wekyb3d8bbwe", "Terminal", _installDir)), blocked);

        var entry = Assert.Single(provider.Load().Value);
        Assert.Equal("Terminal – Open in Terminal", entry.DisplayName);
        Assert.Contains(@"Directory\Background", entry.Detail);
        Assert.Equal(EntryState.Enabled, entry.State);

        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);
        Assert.True(_sandbox.HasValue("UserBlocked", Clsid));
        Assert.Equal(EntryState.Disabled, Assert.Single(provider.Load().Value).State);

        Assert.True(provider.SetState(entry, EntryState.Enabled).IsSuccess);
        Assert.False(_sandbox.HasValue("UserBlocked", Clsid));
    }

    [Fact]
    public void Machine_wide_block_counts_as_disabled_and_unblock_removes_it()
    {
        _sandbox.Create("MachineBlocked", (Clsid.ToLowerInvariant(), ""));
        var blocked = new BlockedShellExtensions(_sandbox.Root / "UserBlocked", _sandbox.Root / "MachineBlocked");

        Assert.True(blocked.IsBlocked(Guid.Parse(Clsid)));
        Assert.True(blocked.Unblock(Guid.Parse(Clsid)).IsSuccess);
        Assert.False(blocked.IsBlocked(Guid.Parse(Clsid)));
    }

    [Fact]
    public void Real_package_source_lists_packages_without_elevation()
    {
        var packages = new WindowsPackageSource().GetPackages();

        Assert.NotEmpty(packages);
        Assert.All(packages, p => Assert.False(string.IsNullOrEmpty(p.FamilyName)));
    }

    private sealed class FakePackages(params InstalledPackage[] packages) : IPackageSource
    {
        public IReadOnlyList<InstalledPackage> GetPackages() => packages;
    }
}
