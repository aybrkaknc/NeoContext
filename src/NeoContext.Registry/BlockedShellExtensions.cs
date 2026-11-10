using Microsoft.Win32;
using NeoContext.Core.Results;

namespace NeoContext.Registry;

/// <summary>A blocked shell extension and the list it is blocked in.</summary>
public sealed record BlockedEntry(Guid Clsid, RegistryPath List, string Label);

/// <summary>
/// The <c>Shell Extensions\Blocked</c> lists. Explorer skips any COM shell extension whose CLSID
/// is a value name in one of these keys. It is the supported way to hide entries that live outside
/// the registry, such as the context menu verbs of packaged (MSIX) apps.
/// <para>
/// NeoContext blocks in the per-user list, so no administrator rights are needed. An entry that is
/// blocked machine-wide (HKLM) can only be unblocked with elevation.
/// </para>
/// </summary>
public sealed class BlockedShellExtensions(RegistryPath userList, RegistryPath machineList)
{
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    public static BlockedShellExtensions Default { get; } = new(
        new RegistryPath(RegistryHive.CurrentUser, SubKey),
        new RegistryPath(RegistryHive.LocalMachine, SubKey));

    public bool IsBlocked(Guid clsid) => Contains(userList, clsid) || Contains(machineList, clsid);

    /// <summary>Every blocked CLSID with the list it is in (per-user or machine-wide).</summary>
    public IReadOnlyList<BlockedEntry> List()
    {
        var found = new List<BlockedEntry>();
        foreach (var (list, label) in new[] { (userList, ClassesRoots.CurrentUserLabel), (machineList, ClassesRoots.AllUsersLabel) })
        {
            using var key = RegistryStore.OpenRead(list);
            foreach (var name in key?.GetValueNames() ?? [])
            {
                if (Guid.TryParse(name, out var clsid))
                {
                    found.Add(new BlockedEntry(clsid, list, label));
                }
            }
        }

        return found;
    }

    /// <summary>Removes a CLSID from one specific list.</summary>
    public static Result Unblock(Guid clsid, RegistryPath list) =>
        RegistryStore.Write(list, key => DeleteMatching(key, clsid));

    public Result Block(Guid clsid) =>
        RegistryStore.Write(userList, key => key.SetValue(Format(clsid), "", RegistryValueKind.String), create: true);

    public Result Unblock(Guid clsid)
    {
        if (Contains(userList, clsid))
        {
            var user = RegistryStore.Write(userList, key => DeleteMatching(key, clsid));
            if (!user.IsSuccess)
            {
                return user;
            }
        }

        return Contains(machineList, clsid)
            ? RegistryStore.Write(machineList, key => DeleteMatching(key, clsid))
            : Result.Success();
    }

    private static bool Contains(RegistryPath list, Guid clsid)
    {
        using var key = RegistryStore.OpenRead(list);
        return key?.GetValueNames().Any(name => Guid.TryParse(name, out var g) && g == clsid) == true;
    }

    /// <summary>Value names may differ in case or braces, so match by parsed GUID.</summary>
    private static void DeleteMatching(RegistryKey key, Guid clsid)
    {
        foreach (var name in key.GetValueNames().Where(n => Guid.TryParse(n, out var g) && g == clsid))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    private static string Format(Guid clsid) => clsid.ToString("B").ToUpperInvariant();
}
