using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Registry.Providers;

/// <summary>
/// COM context menu handlers: <c>&lt;class&gt;\shellex\ContextMenuHandlers\&lt;name&gt;</c>.
/// <para>
/// Disabling moves the handler key to a sibling <c>-ContextMenuHandlers</c> key, which Explorer
/// ignores. This is the same convention the original app used, so entries it disabled show up
/// here as disabled and can be re-enabled.
/// </para>
/// </summary>
public sealed class ShellExtensionProvider(ClassesRoots roots, IIndirectStringResolver resolver) : IEntryProvider
{
    internal const string ActiveKey = "ContextMenuHandlers";
    internal const string DisabledKey = "-ContextMenuHandlers";

    private const EntryCapabilities Capabilities = EntryCapabilities.Toggle | EntryCapabilities.Delete;

    public EntryKind Kind => EntryKind.ShellExtension;

    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var root in roots.Writable)
        {
            foreach (var (section, classKey) in ShellScopes.Keys)
            {
                var shellex = root.Path / classKey / "shellex";
                AddHandlers(entries, section, root, shellex / ActiveKey, EntryState.Enabled);
                AddHandlers(entries, section, root, shellex / DisabledKey, EntryState.Disabled);
            }
        }

        return entries;
    }

    private void AddHandlers(List<ContextMenuEntry> entries, MenuSection section, ClassesRoot root, RegistryPath handlersPath, EntryState state)
    {
        using var handlers = RegistryStore.OpenRead(handlersPath);
        if (handlers is null)
        {
            return;
        }

        foreach (var name in handlers.GetSubKeyNames())
        {
            using var handler = handlers.OpenSubKey(name);

            // The CLSID is usually the default value; some handlers use it as the key name instead.
            if (!TryGetClsid(handler?.GetValue("") as string, name, out var clsid))
            {
                continue;
            }

            var path = handlersPath / name;
            entries.Add(new ContextMenuEntry(
                Id: path.ToString(),
                Kind: EntryKind.ShellExtension,
                Section: section,
                Key: clsid.ToString("B"),
                DisplayName: ResolveName(clsid, name),
                State: state,
                Location: path.ToString(),
                Capabilities: Capabilities)
            {
                Detail = $"{clsid:B} · {root.Label}",
                Command = ResolveServerPath(clsid),
            });
        }
    }

    private static bool TryGetClsid(string? defaultValue, string keyName, out Guid clsid) =>
        Guid.TryParse(defaultValue, out clsid) || Guid.TryParse(keyName, out clsid);

    /// <summary>The handler's own key name, unless it is just the CLSID; then the COM class name.</summary>
    private string ResolveName(Guid clsid, string keyName)
    {
        if (!Guid.TryParse(keyName, out _))
        {
            return keyName;
        }

        var className = RegistryStore.GetString(roots.Merged / "CLSID" / clsid.ToString("B"), "");
        return DisplayText.Clean(className, resolver) ?? clsid.ToString("B");
    }

    /// <summary>The DLL that implements the handler, shown so users can tell where it comes from.</summary>
    private string? ResolveServerPath(Guid clsid) =>
        RegistryStore.GetString(roots.Merged / "CLSID" / clsid.ToString("B") / "InprocServer32", "");

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        var path = RegistryPath.Parse(entry.Location);
        var shellex = path.Parent?.Parent;
        if (shellex is null)
        {
            return Result.Failure(ErrorKind.InvalidInput, $"Unexpected handler location: {path}");
        }

        var target = shellex.Value / (state == EntryState.Enabled ? ActiveKey : DisabledKey) / path.Name;
        return RegistryStore.Move(path, target);
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        Result.Failure(ErrorKind.NotSupported, "Shell extension names come from the COM class and are not renamed.");

    public Result Delete(ContextMenuEntry entry) =>
        RegistryStore.DeleteTree(RegistryPath.Parse(entry.Location));
}
