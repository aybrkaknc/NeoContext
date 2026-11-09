using System.Runtime.InteropServices;
using System.Text;
using NeoContext.Shell.Interop;

namespace NeoContext.Shell.WinX;

/// <summary>
/// Computes and writes the hash that Explorer requires on Win+X shortcuts. Without a valid hash
/// Explorer silently ignores a shortcut, so every change to a Win+X entry must keep it correct.
/// <para>
/// Algorithm (from Rafael Rivera's hashlnk, https://github.com/riverar/hashlnk, also used by
/// ContextMenuManager): lower-case(generalized target + arguments + salt), UTF-16LE encoded,
/// hashed with shlwapi <c>HashData</c>; the first four bytes are the hash.
/// </para>
/// </summary>
public static class WinXHasher
{
    /// <summary>The fixed salt Explorer uses. The double space is intentional.</summary>
    internal const string Salt = "do not prehash links.  this should only be done by the user.";

    /// <summary>Hash for a shortcut target and its arguments.</summary>
    public static uint ComputeHash(string? targetPath, string? arguments, WinXPathGeneralizer generalizer)
    {
        var blob = (generalizer.Generalize(targetPath) + arguments + Salt).ToLowerInvariant();
        var data = Encoding.Unicode.GetBytes(blob);
        var hash = new byte[sizeof(uint)];
        Marshal.ThrowExceptionForHR(NativeMethods.HashData(data, data.Length, hash, hash.Length));
        return BitConverter.ToUInt32(hash, 0);
    }

    /// <summary>Reads the shortcut's target and arguments and returns the hash it should carry.</summary>
    public static uint ComputeHashForShortcut(string lnkPath, WinXPathGeneralizer generalizer)
    {
        using var store = FilePropertyStore.Open(lnkPath, writable: false);
        return ComputeHash(store.GetString(PropertyKey.LinkTargetParsingPath), store.GetString(PropertyKey.LinkArguments), generalizer);
    }

    /// <summary>The hash currently stored in the shortcut, or null if it has none.</summary>
    public static uint? ReadStoredHash(string lnkPath)
    {
        using var store = FilePropertyStore.Open(lnkPath, writable: false);
        return store.GetUInt32(PropertyKey.WinXHash);
    }

    /// <summary>Computes the hash and stores it in the shortcut.</summary>
    public static void Apply(string lnkPath, WinXPathGeneralizer generalizer)
    {
        var hash = ComputeHashForShortcut(lnkPath, generalizer);
        using var store = FilePropertyStore.Open(lnkPath, writable: true);
        store.SetUInt32(PropertyKey.WinXHash, hash);
    }
}

/// <summary>
/// Replaces well-known folder prefixes with their KNOWNFOLDERID, the way Explorer does before hashing.
/// <para>
/// The original used a Dictionary and relied on its enumeration order to test System32 before
/// SystemRoot, and String.Replace replaced every occurrence. Here the prefixes are an explicit list
/// sorted longest first, and only the prefix is replaced.
/// </para>
/// </summary>
public sealed class WinXPathGeneralizer
{
    private readonly (string Prefix, string FolderId)[] _prefixes;

    public WinXPathGeneralizer(IEnumerable<(string Prefix, string FolderId)> prefixes) =>
        _prefixes = prefixes
            .Where(p => !string.IsNullOrEmpty(p.Prefix))
            .Select(p => (p.Prefix.TrimEnd('\\'), p.FolderId))
            .OrderByDescending(p => p.Item1.Length)
            .ToArray();

    /// <summary>The folders Explorer generalizes, resolved on this machine.</summary>
    public static WinXPathGeneralizer ForCurrentSystem() => new(
    [
        (Environment.GetFolderPath(Environment.SpecialFolder.System), "{1ac14e77-02e7-4e5d-b744-2eb1ae5198b7}"),
        (Environment.GetFolderPath(Environment.SpecialFolder.Windows), "{f38bf404-1d43-42f2-9305-67de0b28fc23}"),
        (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "{905e63b6-c1bf-494e-b29c-65b732d3d21a}"),
    ]);

    public string? Generalize(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        foreach (var (prefix, folderId) in _prefixes)
        {
            if (path.StartsWith(prefix + @"\", StringComparison.OrdinalIgnoreCase))
            {
                return folderId + path[prefix.Length..];
            }
        }

        return path;
    }
}
