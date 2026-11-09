using Microsoft.Win32;

namespace NeoContext.Registry;

/// <summary>
/// A registry key location: a hive plus a sub-path. Parses both long (<c>HKEY_CLASSES_ROOT</c>)
/// and short (<c>HKCR</c>) hive names, so paths copied from regedit work as-is.
/// </summary>
public readonly record struct RegistryPath
{
    private static readonly Dictionary<string, RegistryHive> HiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HKEY_CLASSES_ROOT"] = RegistryHive.ClassesRoot,
        ["HKCR"] = RegistryHive.ClassesRoot,
        ["HKEY_CURRENT_USER"] = RegistryHive.CurrentUser,
        ["HKCU"] = RegistryHive.CurrentUser,
        ["HKEY_LOCAL_MACHINE"] = RegistryHive.LocalMachine,
        ["HKLM"] = RegistryHive.LocalMachine,
        ["HKEY_USERS"] = RegistryHive.Users,
        ["HKU"] = RegistryHive.Users,
        ["HKEY_CURRENT_CONFIG"] = RegistryHive.CurrentConfig,
        ["HKCC"] = RegistryHive.CurrentConfig,
    };

    public RegistryPath(RegistryHive hive, string subKey)
    {
        Hive = hive;
        SubKey = subKey.Trim('\\');
    }

    public RegistryHive Hive { get; }

    /// <summary>The path below the hive, without leading or trailing backslashes. Empty for the hive itself.</summary>
    public string SubKey { get; }

    /// <summary>The last path segment, e.g. <c>open</c> for <c>HKCR\*\shell\open</c>.</summary>
    public string Name => SubKey.Length == 0 ? HiveName(Hive) : SubKey[(SubKey.LastIndexOf('\\') + 1)..];

    /// <summary>The containing key, or null for the hive itself.</summary>
    public RegistryPath? Parent
    {
        get
        {
            if (SubKey.Length == 0)
            {
                return null;
            }

            var index = SubKey.LastIndexOf('\\');
            return new RegistryPath(Hive, index < 0 ? "" : SubKey[..index]);
        }
    }

    public static RegistryPath Parse(string path)
    {
        if (!TryParse(path, out var result))
        {
            throw new FormatException($"'{path}' is not a registry path with a known hive.");
        }

        return result;
    }

    public static bool TryParse(string? path, out RegistryPath result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim().Trim('\\');
        var separator = trimmed.IndexOf('\\');
        var hiveName = separator < 0 ? trimmed : trimmed[..separator];
        if (!HiveNames.TryGetValue(hiveName, out var hive))
        {
            return false;
        }

        result = new RegistryPath(hive, separator < 0 ? "" : trimmed[(separator + 1)..]);
        return true;
    }

    /// <summary>Appends one or more segments, e.g. <c>path / "shell" / "open"</c>.</summary>
    public RegistryPath Combine(string child) =>
        new(Hive, SubKey.Length == 0 ? child : $@"{SubKey}\{child.Trim('\\')}");

    public static RegistryPath operator /(RegistryPath path, string child) => path.Combine(child);

    /// <summary>A sibling key with a different last segment.</summary>
    public RegistryPath WithName(string name) => (Parent ?? this).Combine(name);

    public override string ToString() => SubKey.Length == 0 ? HiveName(Hive) : $@"{HiveName(Hive)}\{SubKey}";

    private static string HiveName(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
        RegistryHive.CurrentUser => "HKEY_CURRENT_USER",
        RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
        RegistryHive.Users => "HKEY_USERS",
        RegistryHive.CurrentConfig => "HKEY_CURRENT_CONFIG",
        _ => hive.ToString(),
    };
}
