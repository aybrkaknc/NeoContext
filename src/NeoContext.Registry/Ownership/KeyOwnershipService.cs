using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using NeoContext.Core.Results;

namespace NeoContext.Registry.Ownership;

/// <summary>The security descriptor of a key before NeoContext changed it.</summary>
/// <param name="Path">The key, as a full registry path.</param>
/// <param name="Sddl">Owner, group and DACL in SDDL form.</param>
/// <param name="TakenAt">When ownership was taken (UTC).</param>
public sealed record OwnershipBackup(string Path, string Sddl, DateTimeOffset TakenAt);

/// <summary>
/// Takes ownership of protected keys (for example those owned by TrustedInstaller) and restores them.
/// <para>
/// The original app did this silently, on every read, for whole subtrees, and never restored the old
/// owner. NeoContext only does it when the user agrees after a write failed, for one key at a time,
/// and saves the previous security descriptor first so the change can be undone.
/// </para>
/// </summary>
public sealed class KeyOwnershipService(string backupFilePath)
{
    private const AccessControlSections Sections = AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Makes the current user the owner of <paramref name="path"/> with full control. Needs elevation.</summary>
    public Result<OwnershipBackup> TakeOwnership(RegistryPath path)
    {
        if (!RegistryStore.IsElevated)
        {
            return Result<OwnershipBackup>.Failure(ErrorKind.RequiresElevation, "Taking ownership needs administrator rights.");
        }

        if (path.Hive == RegistryHive.ClassesRoot)
        {
            return Result<OwnershipBackup>.Failure(ErrorKind.InvalidInput, "Use the real hive (HKLM or HKCU), not the merged HKEY_CLASSES_ROOT view.");
        }

        try
        {
            using var privileges = PrivilegeScope.Enable(PrivilegeScope.TakeOwnership, PrivilegeScope.Restore);
            using var hive = RegistryKey.OpenBaseKey(path.Hive, RegistryView.Default);
            var user = WindowsIdentity.GetCurrent().User!;

            OwnershipBackup backup;
            using (var key = hive.OpenSubKey(path.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership | RegistryRights.ReadPermissions))
            {
                if (key is null)
                {
                    return Result<OwnershipBackup>.Failure(ErrorKind.NotFound, $"Registry key not found: {path}");
                }

                var security = key.GetAccessControl(Sections);
                backup = new OwnershipBackup(path.ToString(), security.GetSecurityDescriptorSddlForm(Sections), DateTimeOffset.UtcNow);
                SaveBackup(backup);

                security.SetOwner(user);
                key.SetAccessControl(security);
            }

            using (var key = hive.OpenSubKey(path.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadPermissions)!)
            {
                var security = key.GetAccessControl(AccessControlSections.Access);
                security.AddAccessRule(new RegistryAccessRule(user, RegistryRights.FullControl, InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
                key.SetAccessControl(security);
            }

            return backup;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or IOException)
        {
            return Result<OwnershipBackup>.Failure(ErrorKind.AccessDenied, $"Could not take ownership of {path}: {ex.Message}");
        }
    }

    /// <summary>Puts the saved owner and permissions back and removes the backup.</summary>
    public Result Restore(OwnershipBackup backup)
    {
        if (!RegistryStore.IsElevated)
        {
            return Result.Failure(ErrorKind.RequiresElevation, "Restoring ownership needs administrator rights.");
        }

        var path = RegistryPath.Parse(backup.Path);
        try
        {
            // SeRestorePrivilege allows setting an owner other than ourselves (e.g. TrustedInstaller).
            using var privileges = PrivilegeScope.Enable(PrivilegeScope.TakeOwnership, PrivilegeScope.Restore);
            using var hive = RegistryKey.OpenBaseKey(path.Hive, RegistryView.Default);
            using var key = hive.OpenSubKey(path.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.TakeOwnership | RegistryRights.ChangePermissions | RegistryRights.ReadPermissions);
            if (key is null)
            {
                // The key was deleted since; there is nothing left to restore.
                RemoveBackup(backup);
                return Result.Success();
            }

            var security = new RegistrySecurity();
            security.SetSecurityDescriptorSddlForm(backup.Sddl, Sections);
            key.SetAccessControl(security);
            RemoveBackup(backup);
            return Result.Success();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or IOException)
        {
            return Result.Failure(ErrorKind.AccessDenied, $"Could not restore ownership of {path}: {ex.Message}");
        }
    }

    /// <summary>All keys whose ownership was taken and not yet restored.</summary>
    public IReadOnlyList<OwnershipBackup> GetBackups()
    {
        if (!File.Exists(backupFilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<OwnershipBackup>>(File.ReadAllText(backupFilePath), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveBackup(OwnershipBackup backup)
    {
        // Keep the oldest backup per key: that one holds the original owner.
        var backups = GetBackups().ToList();
        if (backups.Any(b => string.Equals(b.Path, backup.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        backups.Add(backup);
        WriteBackups(backups);
    }

    private void RemoveBackup(OwnershipBackup backup) =>
        WriteBackups(GetBackups().Where(b => !string.Equals(b.Path, backup.Path, StringComparison.OrdinalIgnoreCase)).ToList());

    private void WriteBackups(List<OwnershipBackup> backups)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);
        var temp = backupFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(backups, JsonOptions));
        File.Move(temp, backupFilePath, overwrite: true);
    }
}
