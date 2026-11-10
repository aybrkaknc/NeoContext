using NeoContext.Core.Abstractions;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Registry.Providers;

/// <summary>
/// The "Detailed editing" page: switches for Windows and common programs, from the dictionary. A group is
/// shown only when its program is installed. Number and text settings are not offered yet.
/// </summary>
public sealed class DetailedEditProvider(DetailedEditStore store, IIndirectStringResolver resolver, Func<IReadOnlyList<DetailedGroup>> groups) : IEntryProvider
{
    private Dictionary<string, DetailedItem> _index = [];

    public EntryKind Kind => EntryKind.Detailed;

    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var entries = new List<ContextMenuEntry>();
        var index = new Dictionary<string, DetailedItem>(StringComparer.OrdinalIgnoreCase);
        var groupNumber = 0;
        foreach (var group in groups())
        {
            groupNumber++;
            if (group.Guids.Count > 0 && !group.Guids.Any(store.IsInstalled))
            {
                continue;
            }

            var groupName = DisplayText.Clean(group.Text, resolver) ?? group.RegPath;
            var itemNumber = 0;
            foreach (var item in group.Items)
            {
                itemNumber++;
                if (item.Kind != DetailedKind.Toggle)
                {
                    continue;
                }

                var id = $"detailed:{groupNumber}:{itemNumber}";
                index[id] = item;
                entries.Add(new ContextMenuEntry(
                    Id: id,
                    Kind: EntryKind.Detailed,
                    Section: MenuSection.Detailed,
                    Key: id,
                    DisplayName: DisplayText.Clean(item.Text, resolver) ?? item.Text,
                    State: DetailedEditStore.IsOn(item) ? EntryState.Enabled : EntryState.Disabled,
                    Location: item.Rules[0].RegPath,
                    Capabilities: EntryCapabilities.Toggle)
                {
                    Description = item.Tip,
                    Detail = groupName,
                    IconLocation = group.Icon,
                    RestartExplorer = item.RestartExplorer,
                });
            }
        }

        _index = index;
        return entries;
    }

    public Result SetState(ContextMenuEntry entry, EntryState state) =>
        _index.TryGetValue(entry.Id, out var item)
            ? DetailedEditStore.Set(item, state == EntryState.Enabled)
            : Result.Failure(ErrorKind.NotFound, $"'{entry.DisplayName}' is no longer in the dictionary.");

    public Result Rename(ContextMenuEntry entry, string newName) =>
        Result.Failure(ErrorKind.NotSupported, "Settings cannot be renamed.");

    public Result Delete(ContextMenuEntry entry) =>
        Result.Failure(ErrorKind.NotSupported, "A setting cannot be deleted; switch it off or on.");
}
