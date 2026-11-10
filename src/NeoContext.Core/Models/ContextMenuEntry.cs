namespace NeoContext.Core.Models;

/// <summary>What kind of mechanism puts the entry into a menu. Each kind has its own provider.</summary>
public enum EntryKind
{
    /// <summary>A static verb under <c>...\shell\&lt;verb&gt;</c> with a command line.</summary>
    ShellVerb,

    /// <summary>A COM context menu handler under <c>...\shellex\ContextMenuHandlers</c>.</summary>
    ShellExtension,

    /// <summary>A template in the Explorer "New" submenu (<c>.ext\ShellNew</c>).</summary>
    ShellNew,

    /// <summary>A shortcut in the Win+X (power user) menu.</summary>
    WinX,

    /// <summary>A context menu verb declared by a packaged (MSIX/UWP) app.</summary>
    PackagedApp,
}

/// <summary>Whether an entry currently shows up in its menu.</summary>
public enum EntryState
{
    Enabled,
    Disabled,
}

/// <summary>The menu or object type an entry belongs to. The UI shows one page per section.</summary>
public enum MenuSection
{
    /// <summary>Right click on any file (<c>HKCR\*</c>).</summary>
    Files,

    /// <summary>Right click on any file or folder (<c>HKCR\AllFilesystemObjects</c>).</summary>
    FilesAndFolders,

    /// <summary>Right click on a folder (<c>HKCR\Directory</c>).</summary>
    Folders,

    /// <summary>Right click on the empty area inside a folder (<c>HKCR\Directory\Background</c>).</summary>
    FolderBackground,

    /// <summary>Right click on the desktop (<c>HKCR\DesktopBackground</c>).</summary>
    Desktop,

    /// <summary>Right click on a drive (<c>HKCR\Drive</c>).</summary>
    Drives,

    /// <summary>The Explorer "New" submenu.</summary>
    NewMenu,

    /// <summary>The Win+X menu.</summary>
    WinX,

    /// <summary>Entries that belong to specific file extensions, chosen on the "File types" page.</summary>
    FileTypes,

    /// <summary>Entries contributed by packaged apps.</summary>
    PackagedApps,
}

/// <summary>Operations a provider supports for a specific entry.</summary>
[Flags]
public enum EntryCapabilities
{
    None = 0,
    Toggle = 1,
    Rename = 2,
    Delete = 4,
}

/// <summary>
/// One item in a context menu, as an immutable snapshot. Providers create these on load;
/// after a change the service reloads instead of mutating the snapshot.
/// </summary>
/// <param name="Id">Stable identifier, unique across providers (for example the full registry path).</param>
/// <param name="Kind">The mechanism behind the entry, which selects the provider.</param>
/// <param name="Section">The page the entry is shown on.</param>
/// <param name="Key">The provider's raw key: verb key name, CLSID, extension or file name.</param>
/// <param name="DisplayName">The resolved, human-readable name.</param>
/// <param name="State">Whether the entry is currently visible in its menu.</param>
/// <param name="Location">Where the entry lives (registry path or file path), for display and "open location".</param>
/// <param name="Capabilities">Which operations the provider allows.</param>
public sealed record ContextMenuEntry(
    string Id,
    EntryKind Kind,
    MenuSection Section,
    string Key,
    string DisplayName,
    EntryState State,
    string Location,
    EntryCapabilities Capabilities)
{
    /// <summary>The command line (verbs) or target (shortcuts), when there is one.</summary>
    public string? Command { get; init; }

    /// <summary>What the entry runs or comes from, read from the system (file description, COM class name).</summary>
    public string? Description { get; init; }

    /// <summary>The company that made the program behind the entry, from its file properties.</summary>
    public string? Publisher { get; init; }

    /// <summary>Icon resource location such as <c>shell32.dll,-16</c>.</summary>
    public string? IconLocation { get; init; }

    /// <summary>Extra detail for the UI, e.g. the owning package or a group name.</summary>
    public string? Detail { get; init; }

    /// <summary>Only shown when Shift is held while right-clicking.</summary>
    public bool ExtendedOnly { get; init; }

    public bool IsEnabled => State == EntryState.Enabled;

    public bool Can(EntryCapabilities capability) => (Capabilities & capability) == capability;
}
