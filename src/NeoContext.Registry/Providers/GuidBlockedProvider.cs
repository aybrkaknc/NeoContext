using NeoContext.Core.Abstractions;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;

namespace NeoContext.Registry.Providers;

/// <summary>
/// The "Blocked GUIDs" page: shell extensions Explorer is told to skip, from the per-user and the machine-wide
/// <c>Shell Extensions\Blocked</c> lists. Removing an entry unblocks the extension (a machine-wide one needs
/// administrator rights). Adding a block goes to the per-user list.
/// </summary>
public sealed class GuidBlockedProvider(BlockedShellExtensions blocked, ClassesRoots roots, IIndirectStringResolver resolver, GuidInfoCatalog? guidInfo = null) : IEntryProvider
{
    public EntryKind Kind => EntryKind.GuidBlocked;

    public bool ChangesRequireExplorerRestart => true;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var item in blocked.List())
        {
            var id = item.Clsid.ToString("B");
            var server = RegistryStore.GetString(roots.Merged / "CLSID" / id / "InprocServer32", "");
            var className = DisplayText.Clean(RegistryStore.GetString(roots.Merged / "CLSID" / id, ""), resolver);
            var known = guidInfo?.Find(item.Clsid) is { } info ? DisplayText.FirstReadable(resolver, [.. info.Names]) : null;

            entries.Add(new ContextMenuEntry(
                Id: $"blocked:{item.Label}:{id}",
                Kind: EntryKind.GuidBlocked,
                Section: MenuSection.GuidBlocked,
                Key: id,
                DisplayName: known ?? className ?? id,
                State: EntryState.Disabled,
                Location: item.List.ToString(),
                Capabilities: EntryCapabilities.Delete)
            {
                Command = server,
                Description = FileDescriptions.Describe(server) ?? className,
                Publisher = FileDescriptions.Publisher(server),
                IconLocation = guidInfo?.Find(item.Clsid)?.Icon,
                Detail = item.Label,
            });
        }

        return entries;
    }

    public Result SetState(ContextMenuEntry entry, EntryState state) =>
        Result.Failure(ErrorKind.NotSupported, "Remove the entry to unblock the extension.");

    public Result Rename(ContextMenuEntry entry, string newName) =>
        Result.Failure(ErrorKind.NotSupported, "A blocked entry cannot be renamed.");

    /// <summary>Unblocks the extension in the list the entry came from.</summary>
    public Result Delete(ContextMenuEntry entry) =>
        Guid.TryParse(entry.Key, out var clsid) && RegistryPath.TryParse(entry.Location, out var list)
            ? BlockedShellExtensions.Unblock(clsid, list)
            : Result.Failure(ErrorKind.InvalidInput, $"'{entry.Key}' is not a blocked extension.");
}
