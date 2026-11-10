using System.Globalization;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Registry.Providers;

/// <summary>
/// What a right-button drag and drop does by default: the <c>DefaultDropEffect</c> value (1 copy, 2 move,
/// 4 link) on the file and folder classes. The four choices are four entries and exactly one is on; turning
/// one on writes it, turning the active one off goes back to Windows' own choice.
/// <para>
/// The texts come from <paramref name="text"/>, so the app can translate them: keys are
/// <c>DropEffect_&lt;Default|Copy|Move|Link&gt;</c> and <c>DropEffect_&lt;...&gt;_Tip</c> and <c>DropEffect_Group</c>.
/// </para>
/// </summary>
public sealed class DropEffectProvider(ClassesRoots roots, Func<string, string> text) : IEntryProvider
{
    private const string ValueName = "DefaultDropEffect";

    private static readonly (string Key, int Value)[] Effects = [("Default", 0), ("Copy", 1), ("Move", 2), ("Link", 4)];

    private static readonly string[] Classes = ["*", "AllFilesystemObjects", "Folder", "Directory"];

    public EntryKind Kind => EntryKind.DropEffect;

    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var current = Current();
        return Effects.Select(effect => new ContextMenuEntry(
            Id: $"dropeffect:{effect.Key}",
            Kind: EntryKind.DropEffect,
            Section: MenuSection.DragDrop,
            Key: effect.Key,
            DisplayName: text($"DropEffect_{effect.Key}"),
            State: effect.Value == current ? EntryState.Enabled : EntryState.Disabled,
            Location: (roots.Merged / Classes[0]).ToString(),
            Capabilities: EntryCapabilities.Toggle)
        {
            Description = text($"DropEffect_{effect.Key}_Tip"),
            Detail = text("DropEffect_Group"),
        }).ToList();
    }

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        var effect = Effects.FirstOrDefault(e => e.Key == entry.Key);
        if (effect.Key is null)
        {
            return Result.Failure(ErrorKind.NotFound, $"Unknown drop effect '{entry.Key}'.");
        }

        var current = Current();
        if (state == EntryState.Enabled)
        {
            return effect.Value == current ? Result.Success() : Apply(effect.Value);
        }

        // Switching the active choice off means "back to Windows' own choice". Switching off a choice that is not
        // active, or the default itself, changes nothing.
        return effect.Value == current && effect.Value != 0 ? Apply(0) : Result.Success();
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        Result.Failure(ErrorKind.NotSupported, "A drop effect cannot be renamed.");

    public Result Delete(ContextMenuEntry entry) =>
        Result.Failure(ErrorKind.NotSupported, "A drop effect cannot be deleted.");

    /// <summary>The value on the first class as Explorer sees it (per-user over machine-wide); anything else counts as the default.</summary>
    private int Current() =>
        int.TryParse(RegistryStore.GetString(roots.Merged / Classes[0], ValueName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        && Effects.Any(e => e.Value == value)
            ? value
            : 0;

    private Result Apply(int value)
    {
        var first = Result.Success();
        foreach (var className in Classes)
        {
            // A real value goes to the per-user class, which wins over the machine-wide one. The default removes the value everywhere.
            foreach (var root in value == 0 ? roots.Writable : roots.Writable.Take(1))
            {
                var path = root.Path / className;
                var result = value == 0
                    ? RegistryStore.DeleteValues(path, ValueName)
                    : RegistryStore.Write(path, key => key.SetValue(ValueName, value, Microsoft.Win32.RegistryValueKind.DWord), create: true);
                if (result.Error?.Kind == ErrorKind.NotFound)
                {
                    result = Result.Success();
                }

                if (!result.IsSuccess && first.IsSuccess)
                {
                    first = result;
                }
            }
        }

        return first;
    }
}
