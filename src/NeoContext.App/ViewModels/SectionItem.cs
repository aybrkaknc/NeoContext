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

    public static IReadOnlyList<SectionGroup> Groups { get; } =
    [
        new("Group_Home", "",
        [
            new(MenuSection.Files, ""),
            new(MenuSection.Folder, ""),
            new(MenuSection.Directory, ""),
            new(MenuSection.FolderBackground, ""),
            new(MenuSection.Desktop, ""),
            new(MenuSection.Drives, ""),
            new(MenuSection.FilesAndFolders, ""),
            new(MenuSection.Computer, ""),
            new(MenuSection.RecycleBin, ""),
            new(MenuSection.Library, ""),
            new(MenuSection.NewMenu, ""),
            new(MenuSection.SendTo, ""),
            new(MenuSection.OpenWith, ""),
            new(MenuSection.WinX, ""),
            new(MenuSection.PackagedApps, ""),
        ]),
        new("Group_FileTypes", "", [new(MenuSection.FileTypes, "")]),
        new("Group_OtherRules", "", [new(MenuSection.Enhance, "")]),
    ];

    public static IReadOnlyList<SectionItem> All { get; } = [.. Groups.SelectMany(g => g.Items)];
}

/// <summary>A group of pages in the navigation pane. A group with a single page is shown as that page.</summary>
public sealed class SectionGroup(string titleKey, string glyph, IReadOnlyList<SectionItem> items)
{
    public string Title { get; } = Strings.Get(titleKey);

    public string Glyph { get; } = glyph;

    public IReadOnlyList<SectionItem> Items { get; } = items;
}
