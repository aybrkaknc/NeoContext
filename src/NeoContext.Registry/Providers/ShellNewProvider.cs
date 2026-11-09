using Microsoft.Win32;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Registry.Providers;

/// <summary>
/// Items in Explorer's "New" submenu. Each one is a <c>ShellNew</c> key under a file extension,
/// either directly (<c>.txt\ShellNew</c>) or under its ProgID (<c>.txt\txtfile\ShellNew</c>).
/// <para>
/// Disabling renames the key to <c>-ShellNew</c>, the same convention as the original app.
/// Renaming is not offered: the only name source Explorer honours for most types is the ProgID's
/// FriendlyTypeName, and changing that would rename the file type everywhere in Windows.
/// </para>
/// </summary>
public sealed class ShellNewProvider(ClassesRoots roots, IIndirectStringResolver resolver, RegistryPath? explorerCache = null) : IEntryProvider
{
    internal const string ActiveKey = "ShellNew";
    internal const string DisabledKey = "-ShellNew";

    /// <summary>A ShellNew key needs one of these values, otherwise Explorer does not show it.</summary>
    internal static readonly string[] TemplateValueNames = ["NullFile", "Data", "FileName", "Directory", "Command", "Handler"];

    /// <summary>Explorer caches the list of New-menu types here and rebuilds it when it is missing.</summary>
    public static readonly RegistryPath DefaultExplorerCache =
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew");

    private const EntryCapabilities Capabilities = EntryCapabilities.Toggle | EntryCapabilities.Delete;

    private readonly RegistryPath _explorerCache = explorerCache ?? DefaultExplorerCache;

    public EntryKind Kind => EntryKind.ShellNew;

    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var root in roots.Writable)
        {
            using var classes = RegistryStore.OpenRead(root.Path);
            if (classes is null)
            {
                continue;
            }

            foreach (var extension in classes.GetSubKeyNames().Where(n => n.StartsWith('.')))
            {
                using var extKey = classes.OpenSubKey(extension);
                if (extKey is null)
                {
                    continue;
                }

                var progId = extKey.GetValue("") as string;
                foreach (var relative in CandidatePaths(progId))
                {
                    using var templateKey = extKey.OpenSubKey(relative);
                    if (templateKey is null || !templateKey.GetValueNames().Intersect(TemplateValueNames, StringComparer.OrdinalIgnoreCase).Any())
                    {
                        continue;
                    }

                    var path = root.Path / extension / relative;
                    var state = path.Name.Equals(DisabledKey, StringComparison.OrdinalIgnoreCase) ? EntryState.Disabled : EntryState.Enabled;
                    entries.Add(new ContextMenuEntry(
                        Id: path.ToString(),
                        Kind: EntryKind.ShellNew,
                        Section: MenuSection.NewMenu,
                        Key: extension,
                        DisplayName: ResolveName(templateKey, progId, extension),
                        State: state,
                        Location: path.ToString(),
                        Capabilities: Capabilities)
                    {
                        Detail = $"{extension} · {root.Label}",
                        IconLocation = templateKey.GetValue("IconPath") as string,
                    });
                }
            }
        }

        return entries;
    }

    private static IEnumerable<string> CandidatePaths(string? progId)
    {
        if (!string.IsNullOrEmpty(progId))
        {
            yield return $@"{progId}\{ActiveKey}";
            yield return $@"{progId}\{DisabledKey}";
        }

        yield return ActiveKey;
        yield return DisabledKey;
    }

    /// <summary>MenuText (only as a resource string), then the type's FriendlyTypeName, then its description.</summary>
    private string ResolveName(RegistryKey templateKey, string? progId, string extension)
    {
        var menuText = templateKey.GetValue("MenuText") as string;
        if (menuText?.StartsWith('@') == true && DisplayText.Clean(menuText, resolver) is { } fromMenuText)
        {
            return fromMenuText;
        }

        if (!string.IsNullOrEmpty(progId))
        {
            var typeKey = roots.Merged / progId;
            var name = DisplayText.FirstReadable(resolver,
                RegistryStore.GetString(typeKey, "FriendlyTypeName"),
                RegistryStore.GetString(typeKey, ""));
            if (name is not null)
            {
                return name;
            }
        }

        return extension;
    }

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        var path = RegistryPath.Parse(entry.Location);
        var result = RegistryStore.Move(path, path.WithName(state == EntryState.Enabled ? ActiveKey : DisabledKey));
        if (result.IsSuccess)
        {
            InvalidateExplorerCache();
        }

        return result;
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        Result.Failure(ErrorKind.NotSupported, "Renaming would change the file type name everywhere in Windows.");

    public Result Delete(ContextMenuEntry entry)
    {
        var result = RegistryStore.DeleteTree(RegistryPath.Parse(entry.Location));
        if (result.IsSuccess)
        {
            InvalidateExplorerCache();
        }

        return result;
    }

    /// <summary>Removes Explorer's cached type list so the next "New" menu reflects the change.</summary>
    private void InvalidateExplorerCache() => RegistryStore.DeleteValues(_explorerCache, "Classes");
}
