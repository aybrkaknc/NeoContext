using NeoContext.Core.Abstractions;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;

namespace NeoContext.Registry.Providers;

/// <summary>
/// COM context menu handlers: <c>&lt;class&gt;\shellex\ContextMenuHandlers\&lt;name&gt;</c>.
/// <para>
/// Disabling moves the handler key to a sibling <c>-ContextMenuHandlers</c> key, which Explorer
/// ignores. This is the same convention the original app used, so entries it disabled show up
/// here as disabled and can be re-enabled.
/// </para>
/// </summary>
public sealed class ShellExtensionProvider(
    ClassesRoots roots,
    IIndirectStringResolver resolver,
    GuidInfoCatalog? guidInfo = null,
    Func<string?>? customPath = null) : IEntryProvider
{
    internal const string ActiveKey = "ContextMenuHandlers";
    internal const string DisabledKey = "-ContextMenuHandlers";
    internal const string DragDropKey = "DragDropHandlers";

    private const EntryCapabilities Capabilities = EntryCapabilities.Toggle | EntryCapabilities.Delete;

    public EntryKind Kind => EntryKind.ShellExtension;

    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var (section, classKeys) in ShellScopes.Keys)
        {
            foreach (var classKey in classKeys)
            {
                entries.AddRange(LoadClass(section, classKey));
            }
        }

        foreach (var classKey in ShellScopes.DragDropClasses)
        {
            entries.AddRange(LoadClass(MenuSection.DragDrop, classKey, DragDropKey));
        }

        if (RegistryPath.TryParse(customPath?.Invoke(), out var custom))
        {
            var shellex = custom / "shellex";
            var label = ShellVerbProvider.ScopeLabel(custom);
            AddHandlers(entries, MenuSection.CustomPath, label, shellex / ActiveKey, EntryState.Enabled);
            AddHandlers(entries, MenuSection.CustomPath, label, shellex / DisabledKey, EntryState.Disabled);
        }

        return entries;
    }

    /// <summary>Handlers under <c>&lt;class key&gt;\shellex</c> in every writable root. Also used for per-extension classes.</summary>
    public List<ContextMenuEntry> LoadClass(MenuSection section, string classKey) => LoadClass(section, classKey, ActiveKey);

    /// <summary>
    /// Handlers in <paramref name="handlerKey"/> (<c>ContextMenuHandlers</c> or <c>DragDropHandlers</c>) and in its
    /// disabled twin with a leading minus sign.
    /// </summary>
    private List<ContextMenuEntry> LoadClass(MenuSection section, string classKey, string handlerKey)
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var root in roots.Writable)
        {
            var shellex = root.Path / classKey / "shellex";
            AddHandlers(entries, section, root.Label, shellex / handlerKey, EntryState.Enabled);
            AddHandlers(entries, section, root.Label, shellex / $"-{handlerKey}", EntryState.Disabled);
        }

        return entries;
    }

    private void AddHandlers(List<ContextMenuEntry> entries, MenuSection section, string label, RegistryPath handlersPath, EntryState state)
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
            var server = ResolveServerPath(clsid);
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
                Detail = label,
                Command = server,
                Description = FileDescriptions.Describe(server) ?? ClassName(clsid),
                Publisher = FileDescriptions.Publisher(server),
                IconLocation = guidInfo?.Find(clsid)?.Icon ?? InstalledApps.FindIcon(server),
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

        // The dictionary names the menu item itself, which says more than the COM class name.
        if (guidInfo?.Find(clsid) is { } info && DisplayText.FirstReadable(resolver, [.. info.Names]) is { } known)
        {
            return known;
        }

        var className = RegistryStore.GetString(roots.Merged / "CLSID" / clsid.ToString("B"), "");
        return DisplayText.Clean(className, resolver) ?? clsid.ToString("B");
    }

    private string? ClassName(Guid clsid) =>
        DisplayText.Clean(RegistryStore.GetString(roots.Merged / "CLSID" / clsid.ToString("B"), ""), resolver);

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

        // The key the handler sits in says which pair it belongs to: ContextMenuHandlers or DragDropHandlers.
        var activeKey = path.Parent?.Name.TrimStart('-') ?? ActiveKey;
        var target = shellex.Value / (state == EntryState.Enabled ? activeKey : $"-{activeKey}") / path.Name;
        return RegistryStore.Move(path, target);
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        Result.Failure(ErrorKind.NotSupported, "Shell extension names come from the COM class and are not renamed.");

    public Result Delete(ContextMenuEntry entry) =>
        RegistryStore.DeleteTree(RegistryPath.Parse(entry.Location));
}
