using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using NeoContext.Core.Results;

namespace NeoContext.Registry;

/// <summary>
/// Thin, exception-free wrapper over <see cref="Microsoft.Win32.Registry"/>.
/// Reads never change permissions (unlike the original app, which took ownership on every open).
/// Writes translate access problems into <see cref="ErrorKind.RequiresElevation"/> or
/// <see cref="ErrorKind.AccessDenied"/> so the UI can offer the right fix.
/// </summary>
public static class RegistryStore
{
    /// <summary>True when the current process runs as administrator.</summary>
    public static bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Opens a key for reading. Returns null when it does not exist or cannot be read.</summary>
    public static RegistryKey? OpenRead(RegistryPath path)
    {
        try
        {
            var hive = RegistryKey.OpenBaseKey(path.Hive, RegistryView.Default);
            if (path.SubKey.Length == 0)
            {
                return hive;
            }

            using (hive)
            {
                return hive.OpenSubKey(path.SubKey, writable: false);
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public static bool Exists(RegistryPath path)
    {
        using var key = OpenRead(path);
        return key is not null;
    }

    /// <summary>Reads one value as a string, or null when the key or value is missing.</summary>
    public static string? GetString(RegistryPath path, string valueName)
    {
        using var key = OpenRead(path);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
    }

    /// <summary>Runs <paramref name="action"/> on a writable key, creating it when <paramref name="create"/> is true.</summary>
    public static Result Write(RegistryPath path, Action<RegistryKey> action, bool create = false) => Guard(path, () =>
    {
        using var hive = RegistryKey.OpenBaseKey(path.Hive, RegistryView.Default);
        using var key = create ? hive.CreateSubKey(path.SubKey, writable: true) : hive.OpenSubKey(path.SubKey, writable: true);
        if (key is null)
        {
            return Result.Failure(ErrorKind.NotFound, $"Registry key not found: {path}");
        }

        action(key);
        return Result.Success();
    });

    public static Result SetString(RegistryPath path, string valueName, string value, bool create = false) =>
        Write(path, key => key.SetValue(valueName, value, RegistryValueKind.String), create);

    /// <summary>Deletes the listed values. Values that do not exist are ignored.</summary>
    public static Result DeleteValues(RegistryPath path, params string[] valueNames) =>
        Write(path, key =>
        {
            foreach (var name in valueNames)
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
        });

    /// <summary>Deletes a key and everything below it. Succeeds when the key is already gone.</summary>
    public static Result DeleteTree(RegistryPath path) => Guard(path, () =>
    {
        if (path.SubKey.Length == 0)
        {
            return Result.Failure(ErrorKind.InvalidInput, "Refusing to delete a registry hive.");
        }

        using var hive = RegistryKey.OpenBaseKey(path.Hive, RegistryView.Default);
        hive.DeleteSubKeyTree(path.SubKey, throwOnMissingSubKey: false);
        return Result.Success();
    });

    /// <summary>
    /// Moves a key to <paramref name="destination"/> by copying and then deleting the source.
    /// The registry has no transactions here, so if the delete fails the copy is removed again
    /// and the registry is left as it was.
    /// </summary>
    public static Result Move(RegistryPath source, RegistryPath destination)
    {
        if (Exists(destination))
        {
            return Result.Failure(ErrorKind.InvalidInput, $"Cannot move to {destination}: a key with that name already exists.");
        }

        var copy = Guard(source, () =>
        {
            using var from = OpenRead(source);
            if (from is null)
            {
                return Result.Failure(ErrorKind.NotFound, $"Registry key not found: {source}");
            }

            using var hive = RegistryKey.OpenBaseKey(destination.Hive, RegistryView.Default);
            using var to = hive.CreateSubKey(destination.SubKey, writable: true);
            CopyTree(from, to);
            return Result.Success();
        });

        if (!copy.IsSuccess)
        {
            DeleteTree(destination);
            return copy;
        }

        var delete = DeleteTree(source);
        if (!delete.IsSuccess)
        {
            DeleteTree(destination);
        }

        return delete;
    }

    private static void CopyTree(RegistryKey from, RegistryKey to)
    {
        foreach (var name in from.GetValueNames())
        {
            var value = from.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is not null)
            {
                to.SetValue(name, value, from.GetValueKind(name));
            }
        }

        foreach (var childName in from.GetSubKeyNames())
        {
            using var child = from.OpenSubKey(childName, writable: false);
            using var target = to.CreateSubKey(childName, writable: true);
            if (child is not null)
            {
                CopyTree(child, target);
            }
        }
    }

    /// <summary>Converts expected registry exceptions into results.</summary>
    private static Result Guard(RegistryPath path, Func<Result> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return IsElevated
                ? Result.Failure(ErrorKind.AccessDenied, $"Access denied to {path}. Its owner or permissions block this change.")
                : Result.Failure(ErrorKind.RequiresElevation, $"Administrator rights are needed to change {path}.");
        }
        catch (IOException ex)
        {
            return Result.Failure(ErrorKind.Unexpected, $"{path}: {ex.Message}");
        }
    }
}
