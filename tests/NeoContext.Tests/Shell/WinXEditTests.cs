using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;
using NeoContext.Core.Services;
using NeoContext.Shell;
using NeoContext.Shell.Interop;
using NeoContext.Shell.WinX;

namespace NeoContext.Tests.Shell;

/// <summary>Creating Win+X shortcuts, moving them up and down, and restoring the factory menu in a temporary folder.</summary>
public sealed class WinXEditTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("neocontext-winx-edit-").FullName;
    private readonly WinXPathGeneralizer _generalizer = WinXPathGeneralizer.ForCurrentSystem();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private WinXProvider Provider(string? root = null) => new(root ?? _root, NullIndirectStringResolver.Instance, _generalizer);

    private static string Notepad => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

    private string Group(string name = "Group1") => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private string Shortcut(string folder, string fileName, string display)
    {
        var path = Path.Combine(folder, fileName);
        using (var link = ShellLink.Create())
        {
            link.TargetPath = Notepad;
            link.Description = display;
            link.SaveAs(path);
        }

        DesktopIni.SetLocalizedName(path, display);
        WinXHasher.Apply(path, _generalizer);
        return path;
    }

    private static string[] Names(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*.lnk").Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)];

    [Theory]
    [InlineData("02 - Run", 2, "Run")]
    [InlineData("10-Tools", 10, "Tools")]
    [InlineData("Plain", null, "Plain")]
    public void Names_split_into_a_number_and_a_text(string name, int? number, string text) =>
        Assert.Equal((number, text), WinXProvider.SplitName(name));

    [Fact]
    public void A_new_shortcut_goes_to_the_end_of_the_group_with_a_name_and_a_valid_hash()
    {
        var folder = Group();
        Shortcut(folder, "01 - First.lnk", "First");
        Shortcut(folder, "02 - Second.lnk", "Second");

        var result = Provider().CreateShortcut("Group1", "My tool", Notepad, "/x");

        Assert.True(result.IsSuccess, result.Error?.Message);
        var created = Path.Combine(folder, "03 - My tool.lnk");
        Assert.True(File.Exists(created));
        Assert.Equal("My tool", DesktopIni.GetLocalizedName(created));
        Assert.Equal(WinXHasher.ComputeHashForShortcut(created, _generalizer), WinXHasher.ReadStoredHash(created));
        Assert.Equal("My tool", Assert.Single(Provider().Load().Value, e => e.Key == "03 - My tool.lnk").DisplayName);
    }

    [Fact]
    public void A_new_shortcut_in_an_empty_group_starts_at_one_and_bad_input_is_refused()
    {
        Group("Group2");

        Assert.True(Provider().CreateShortcut("Group2", "", Notepad, null).IsSuccess);
        Assert.True(File.Exists(Path.Combine(_root, "Group2", "01 - notepad.lnk")));
        Assert.Equal(ErrorKind.NotFound, Provider().CreateShortcut("Nope", "x", Notepad, null).Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, Provider().CreateShortcut("Group2", "x", Path.Combine(_root, "missing.exe"), null).Error!.Kind);
    }

    [Fact]
    public void Moving_down_swaps_with_the_next_item_and_keeps_names_and_hashes()
    {
        var folder = Group();
        Shortcut(folder, "01 - Alpha.lnk", "Alpha name");
        Shortcut(folder, "02 - Beta.lnk", "Beta name");
        Shortcut(folder, "03 - Gamma.lnk", "Gamma name");
        var provider = Provider();
        var alpha = Assert.Single(provider.Load().Value, e => e.Key == "01 - Alpha.lnk");

        Assert.True(provider.Move(alpha, +1).IsSuccess);

        Assert.Equal(["01 - Beta.lnk", "02 - Alpha.lnk", "03 - Gamma.lnk"], Names(folder));
        Assert.Equal("Alpha name", DesktopIni.GetLocalizedName(Path.Combine(folder, "02 - Alpha.lnk")));
        Assert.Equal("Beta name", DesktopIni.GetLocalizedName(Path.Combine(folder, "01 - Beta.lnk")));
        Assert.Null(DesktopIni.GetLocalizedName(Path.Combine(folder, "01 - Alpha.lnk")));
        Assert.Equal(
            WinXHasher.ComputeHashForShortcut(Path.Combine(folder, "02 - Alpha.lnk"), _generalizer),
            WinXHasher.ReadStoredHash(Path.Combine(folder, "02 - Alpha.lnk")));
    }

    [Fact]
    public void Moving_up_works_and_moving_past_either_end_changes_nothing()
    {
        var folder = Group();
        Shortcut(folder, "01 - Alpha.lnk", "Alpha");
        Shortcut(folder, "02 - Beta.lnk", "Beta");
        var provider = Provider();

        Assert.True(provider.Move(Assert.Single(provider.Load().Value, e => e.Key == "01 - Alpha.lnk"), -1).IsSuccess);
        Assert.Equal(["01 - Alpha.lnk", "02 - Beta.lnk"], Names(folder));

        Assert.True(provider.Move(Assert.Single(provider.Load().Value, e => e.Key == "02 - Beta.lnk"), +1).IsSuccess);
        Assert.Equal(["01 - Alpha.lnk", "02 - Beta.lnk"], Names(folder));

        Assert.True(provider.Move(Assert.Single(provider.Load().Value, e => e.Key == "02 - Beta.lnk"), -1).IsSuccess);
        Assert.Equal(["01 - Beta.lnk", "02 - Alpha.lnk"], Names(folder));
    }

    [Fact]
    public void Moving_renumbers_a_group_whose_files_have_no_numbers()
    {
        var folder = Group();
        Shortcut(folder, "Apple.lnk", "Apple");
        Shortcut(folder, "Banana.lnk", "Banana");
        var provider = Provider();

        Assert.True(provider.Move(Assert.Single(provider.Load().Value, e => e.Key == "Apple.lnk"), +1).IsSuccess);

        Assert.Equal(["01 - Banana.lnk", "02 - Apple.lnk"], Names(folder));
    }

    [Fact]
    public void The_service_routes_moves_only_to_entries_that_can_move()
    {
        var folder = Group();
        Shortcut(folder, "01 - A.lnk", "A");
        Shortcut(folder, "02 - B.lnk", "B");
        var service = new ContextMenuService([Provider()], new ProtectionPolicy(enabled: true));
        service.Refresh();
        var a = Assert.Single(service.Entries, e => e.Key == "01 - A.lnk");

        Assert.True(service.Move(a, +1).IsSuccess);
        Assert.Equal(["01 - B.lnk", "02 - A.lnk"], Names(folder));
        Assert.Equal(ErrorKind.NotSupported, service.Move(a with { Capabilities = EntryCapabilities.Toggle }, +1).Error!.Kind);
    }

    [Fact]
    public void Restoring_defaults_backs_up_the_current_menu_and_copies_the_factory_one()
    {
        var folder = Group();
        Shortcut(folder, "01 - Mine.lnk", "Mine");
        var factory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"neocontext-factory-{Guid.NewGuid():N}")).FullName;
        var backups = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"neocontext-backup-{Guid.NewGuid():N}")).FullName;
        try
        {
            var factoryGroup = Directory.CreateDirectory(Path.Combine(factory, "Group1")).FullName;
            Shortcut(factoryGroup, "01 - Original.lnk", "Original");

            var result = Provider().RestoreDefaults(backups, factory);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(["01 - Original.lnk"], Names(folder));
            var saved = Assert.Single(Directory.EnumerateFiles(backups, "01 - Mine.lnk", SearchOption.AllDirectories));
            Assert.Contains("WinX", saved);
        }
        finally
        {
            Directory.Delete(factory, recursive: true);
            Directory.Delete(backups, recursive: true);
        }
    }

    [Fact]
    public void Restoring_defaults_without_a_factory_folder_changes_nothing()
    {
        var folder = Group();
        Shortcut(folder, "01 - Mine.lnk", "Mine");

        var result = Provider().RestoreDefaults(Path.Combine(_root, "backups"), Path.Combine(_root, "no-factory"));

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Equal(["01 - Mine.lnk"], Names(folder));
    }

    [Fact]
    public void The_menu_order_follows_the_file_order_inside_a_group()
    {
        var entries = new[]
        {
            Entry("Zeta", "02 - Zeta.lnk"),
            Entry("Alpha", "10 - Alpha.lnk"),
            Entry("Beta", "01 - Beta.lnk"),
        };

        var shown = EntryFilter.Apply(entries, "", MenuSection.WinX);

        Assert.Equal(["Beta", "Zeta", "Alpha"], shown.Select(e => e.DisplayName));
    }

    private static ContextMenuEntry Entry(string name, string file) =>
        new($@"C:\WinX\Group1\{file}", EntryKind.WinX, MenuSection.WinX, file, name, EntryState.Enabled, $@"C:\WinX\Group1\{file}", EntryCapabilities.Move)
        {
            Detail = "Group1",
        };
}
