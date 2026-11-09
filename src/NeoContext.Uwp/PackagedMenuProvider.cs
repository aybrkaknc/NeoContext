using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Registry;

namespace NeoContext.Uwp;

/// <summary>An installed package, reduced to what the provider needs.</summary>
public sealed record InstalledPackage(string FamilyName, string DisplayName, string InstalledPath);

/// <summary>Lists installed packages. Abstracted so tests can supply fake packages.</summary>
public interface IPackageSource
{
    IReadOnlyList<InstalledPackage> GetPackages();
}

/// <summary>
/// Context menu verbs contributed by packaged apps. These do not live in the registry, so they
/// are enabled and disabled through the <see cref="BlockedShellExtensions"/> list by CLSID.
/// One entry is shown per package and COM class; blocking a class hides all of its verbs.
/// </summary>
public sealed class PackagedMenuProvider(IPackageSource packages, BlockedShellExtensions blocked) : IEntryProvider
{
    public EntryKind Kind => EntryKind.PackagedApp;

    public bool ChangesRequireExplorerRestart => true;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        IReadOnlyList<InstalledPackage> installed;
        try
        {
            installed = packages.GetPackages();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return Result<IReadOnlyList<ContextMenuEntry>>.Failure(ErrorKind.Unexpected, $"Could not list installed packages: {ex.Message}");
        }

        var entries = new List<ContextMenuEntry>();
        foreach (var package in installed)
        {
            var byClass = AppxManifestReader.ReadFromInstallFolder(package.InstalledPath).GroupBy(v => v.Clsid);
            foreach (var verbs in byClass)
            {
                var clsid = verbs.Key;
                var itemTypes = Summarize(verbs.Select(v => v.ItemType).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                var className = verbs.Select(v => v.ClassDisplayName).FirstOrDefault(n => n is not null);

                entries.Add(new ContextMenuEntry(
                    Id: $"packaged:{package.FamilyName}:{clsid:B}",
                    Kind: EntryKind.PackagedApp,
                    Section: MenuSection.PackagedApps,
                    Key: clsid.ToString("B"),
                    DisplayName: className is null ? package.DisplayName : $"{package.DisplayName} – {className}",
                    State: blocked.IsBlocked(clsid) ? EntryState.Disabled : EntryState.Enabled,
                    Location: package.InstalledPath,
                    Capabilities: EntryCapabilities.Toggle)
                {
                    Detail = itemTypes,
                });
            }
        }

        return entries;
    }

    /// <summary>"*, Directory" stays as is; long lists of extensions become ".jpg, .png, .gif +40".</summary>
    internal static string Summarize(IReadOnlyList<string> itemTypes, int shown = 3) =>
        itemTypes.Count <= shown + 1
            ? string.Join(", ", itemTypes)
            : $"{string.Join(", ", itemTypes.Take(shown))} +{itemTypes.Count - shown}";

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        if (!Guid.TryParse(entry.Key, out var clsid))
        {
            return Result.Failure(ErrorKind.InvalidInput, $"'{entry.Key}' is not a CLSID.");
        }

        return state == EntryState.Disabled ? blocked.Block(clsid) : blocked.Unblock(clsid);
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        Result.Failure(ErrorKind.NotSupported, "Packaged app verbs cannot be renamed.");

    public Result Delete(ContextMenuEntry entry) =>
        Result.Failure(ErrorKind.NotSupported, "Uninstall the app to remove its verbs.");
}
