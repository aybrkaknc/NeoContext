using NeoContext.Core.Abstractions;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Registry.Providers;

/// <summary>
/// The "Ready-made menus" page: every item in the dictionary is one entry. Turning it on writes the item's
/// registry keys, turning it off removes them. Entries can only be toggled; the dictionary decides the rest.
/// </summary>
public sealed class EnhanceMenuProvider(EnhanceMenuStore store, IIndirectStringResolver resolver, Func<IReadOnlyList<EnhanceGroup>> groups) : IEntryProvider
{
    private Dictionary<string, (EnhanceGroup Group, EnhanceItem Item)> _index = [];

    public EntryKind Kind => EntryKind.Enhance;

    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var entries = new List<ContextMenuEntry>();
        var index = new Dictionary<string, (EnhanceGroup, EnhanceItem)>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups().Where(EnhanceMenuStore.IsSupported))
        {
            foreach (var item in group.Items)
            {
                var id = $"enhance:{group.RegPath}:{item.Kind}:{item.KeyName}";
                index[id] = (group, item);
                entries.Add(new ContextMenuEntry(
                    Id: id,
                    Kind: EntryKind.Enhance,
                    Section: MenuSection.Enhance,
                    Key: item.KeyName,
                    DisplayName: DisplayText.Clean(item.Text, resolver) ?? item.KeyName,
                    State: store.IsEnabled(group, item) ? EntryState.Enabled : EntryState.Disabled,
                    Location: EnhanceMenuStore.Location(group, item),
                    Capabilities: EntryCapabilities.Toggle)
                {
                    Command = store.MainCommand(group, item),
                    Description = item.Tip,
                    IconLocation = item.Icon,
                    Detail = DisplayText.Clean(group.Text, resolver) ?? group.Text,
                });
            }
        }

        _index = index;
        return entries;
    }

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        if (!_index.TryGetValue(entry.Id, out var found))
        {
            return Result.Failure(ErrorKind.NotFound, $"'{entry.DisplayName}' is no longer in the dictionary.");
        }

        return state == EntryState.Enabled ? store.Enable(found.Group, found.Item) : store.Disable(found.Group, found.Item);
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        Result.Failure(ErrorKind.NotSupported, "Ready-made items cannot be renamed.");

    public Result Delete(ContextMenuEntry entry) =>
        Result.Failure(ErrorKind.NotSupported, "Turn a ready-made item off instead of deleting it.");
}
