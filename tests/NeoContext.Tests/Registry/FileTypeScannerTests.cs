using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Registry;
using NeoContext.Registry.Providers;

namespace NeoContext.Tests.Registry;

/// <summary>Perceived type, folder type and menu analysis, in a sandbox key.</summary>
public sealed class FileTypeScannerTests : IDisposable
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

    private FileTypeScanner Scanner() => new(
        _sandbox.Classes,
        new ShellVerbProvider(_sandbox.Classes, NullIndirectStringResolver.Instance),
        new ShellExtensionProvider(_sandbox.Classes, NullIndirectStringResolver.Instance),
        _sandbox.Root / "FileExts");

    [Fact]
    public void A_class_is_listed_on_the_chosen_page_with_its_label()
    {
        _sandbox.Create(@"SystemFileAssociations\Image\shell\Edit", ("MUIVerb", "Edit picture"));

        var entry = Assert.Single(Scanner().ScanClass(MenuSection.PerceivedType, @"SystemFileAssociations\Image", "Image"));

        Assert.Equal(MenuSection.PerceivedType, entry.Section);
        Assert.Equal("Edit picture", entry.DisplayName);
        Assert.StartsWith("Image · ", entry.Detail);
    }

    [Fact]
    public void Analysing_a_file_reads_its_classes_and_adds_unknown_only_without_a_program()
    {
        Directory.CreateDirectory(_folder);
        var known = Path.Combine(_folder, "a.xyz");
        var unknown = Path.Combine(_folder, "b.qrs");
        File.WriteAllText(known, "x");
        File.WriteAllText(unknown, "x");
        _sandbox.Create(@"*\shell\AllFiles", ("MUIVerb", "All files"));
        _sandbox.Create(@"Unknown\shell\Pick", ("MUIVerb", "Pick a program"));
        _sandbox.Create(@".xyz", ("", "xyzfile"));
        _sandbox.Create(@"xyzfile\shell\Open", ("MUIVerb", "Open xyz"));
        _sandbox.Create(@"SystemFileAssociations\.xyz\shell\Special", ("MUIVerb", "Special"));

        var forKnown = Scanner().Analyze(known).Select(e => e.DisplayName).ToList();
        var forUnknown = Scanner().Analyze(unknown).Select(e => e.DisplayName).ToList();

        Assert.Contains("All files", forKnown);
        Assert.Contains("Open xyz", forKnown);
        Assert.Contains("Special", forKnown);
        Assert.DoesNotContain("Pick a program", forKnown);
        Assert.Contains("All files", forUnknown);
        Assert.Contains("Pick a program", forUnknown);
        Assert.All(Scanner().Analyze(known), e => Assert.Equal(MenuSection.Analysis, e.Section));
    }

    [Fact]
    public void Analysing_a_folder_reads_the_folder_classes_and_a_missing_path_gives_nothing()
    {
        Directory.CreateDirectory(_folder);
        _sandbox.Create(@"Directory\shell\InFolder", ("MUIVerb", "In folder"));
        _sandbox.Create(@"Folder\shell\AnyFolder", ("MUIVerb", "Any folder"));
        _sandbox.Create(@"Drive\shell\OnDrive", ("MUIVerb", "On drive"));

        var names = Scanner().Analyze(_folder).Select(e => e.DisplayName).ToList();

        Assert.Contains("In folder", names);
        Assert.Contains("Any folder", names);
        Assert.DoesNotContain("On drive", names);
        Assert.Empty(Scanner().Analyze(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public void The_unknown_type_page_covers_the_Unknown_class_and_analysis_stays_out_of_searches()
    {
        Assert.Equal(["Unknown"], NeoContext.Registry.ShellScopes.Keys[MenuSection.Unknown]);

        var entry = new ContextMenuEntry("id", EntryKind.ShellVerb, MenuSection.Analysis, "k", "Findable", EntryState.Enabled, "loc", EntryCapabilities.None);
        Assert.Empty(NeoContext.Core.Rules.EntryFilter.Apply([entry], "Findable", null));
        Assert.Single(NeoContext.Core.Rules.EntryFilter.Apply([entry], "", MenuSection.Analysis));
    }
}
