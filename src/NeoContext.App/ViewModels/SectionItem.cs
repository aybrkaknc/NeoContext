using NeoContext.App.Services;
using NeoContext.Core.Models;

namespace NeoContext.App.ViewModels;

/// <summary>One page in the navigation pane.</summary>
public sealed class SectionItem(MenuSection section, string glyph)
{
    public MenuSection Section { get; } = section;

    /// <summary>A Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; } = glyph;

    public string Title { get; } = Strings.Get($"Section_{section}");

    public string Description { get; } = Strings.Get($"Section_{section}_Description");

    public static IReadOnlyList<SectionItem> All { get; } =
    [
        new(MenuSection.Files, ""),
        new(MenuSection.FileTypes, ""),
        new(MenuSection.Folders, ""),
        new(MenuSection.FolderBackground, ""),
        new(MenuSection.Desktop, ""),
        new(MenuSection.Drives, ""),
        new(MenuSection.FilesAndFolders, ""),
        new(MenuSection.NewMenu, ""),
        new(MenuSection.WinX, ""),
        new(MenuSection.PackagedApps, ""),
    ];
}
