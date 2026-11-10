using Microsoft.Win32;
using NeoContext.Core.Models;

namespace NeoContext.Registry;

/// <summary>A writable classes root and the label shown for its entries.</summary>
public sealed record ClassesRoot(RegistryPath Path, string Label);

/// <summary>
/// Where the providers look for file classes.
/// <para>
/// <c>HKEY_CLASSES_ROOT</c> is a merged view of the per-user and per-machine classes. Writing to it
/// goes to whichever hive holds the key, which is why the original app often needed admin rights.
/// NeoContext reads both hives separately instead, so per-user entries can be changed without
/// elevation and the UI can tell the user which entries affect everyone.
/// </para>
/// </summary>
public sealed record ClassesRoots(IReadOnlyList<ClassesRoot> Writable, RegistryPath Merged)
{
    public const string CurrentUserLabel = "CurrentUser";
    public const string AllUsersLabel = "AllUsers";

    /// <summary>The real Windows locations.</summary>
    public static ClassesRoots Default { get; } = new(
        [
            new ClassesRoot(new RegistryPath(RegistryHive.CurrentUser, @"Software\Classes"), CurrentUserLabel),
            new ClassesRoot(new RegistryPath(RegistryHive.LocalMachine, @"Software\Classes"), AllUsersLabel),
        ],
        new RegistryPath(RegistryHive.ClassesRoot, ""));

    /// <summary>A single root used for both reading and writing. Tests use this with a sandbox key.</summary>
    public static ClassesRoots ForSingleRoot(RegistryPath root) => new([new ClassesRoot(root, CurrentUserLabel)], root);
}

/// <summary>The class key behind each context menu section.</summary>
public static class ShellScopes
{
    /// <summary>A section can span several class keys (a library has three).</summary>
    public static IReadOnlyDictionary<MenuSection, string[]> Keys { get; } = new Dictionary<MenuSection, string[]>
    {
        [MenuSection.Files] = ["*"],
        [MenuSection.FilesAndFolders] = ["AllFilesystemObjects"],
        [MenuSection.Directory] = ["Directory"],
        [MenuSection.Folder] = ["Folder"],
        [MenuSection.FolderBackground] = [@"Directory\Background"],
        [MenuSection.Desktop] = ["DesktopBackground"],
        [MenuSection.Drives] = ["Drive"],
        [MenuSection.Computer] = [@"CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}"],
        [MenuSection.RecycleBin] = [@"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}"],
        [MenuSection.Library] = ["LibraryFolder", @"LibraryFolder\Background", "UserLibraryFolder"],
    };

    /// <summary>The classes whose <c>shellex\DragDropHandlers</c> shape the menu after a right-button drag and drop.</summary>
    public static IReadOnlyList<string> DragDropClasses { get; } = ["Folder", "Directory", "Drive", "AllFilesystemObjects"];
}
