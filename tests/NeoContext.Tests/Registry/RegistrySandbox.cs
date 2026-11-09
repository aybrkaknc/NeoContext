using Microsoft.Win32;
using NeoContext.Registry;

namespace NeoContext.Tests.Registry;

/// <summary>
/// A throw-away key under <c>HKCU\Software\NeoContext.Tests</c>. Tests write fake classes here
/// instead of touching the real context menu, and the key is deleted afterwards.
/// </summary>
public sealed class RegistrySandbox : IDisposable
{
    public RegistrySandbox()
    {
        Root = new RegistryPath(RegistryHive.CurrentUser, $@"Software\NeoContext.Tests\{Guid.NewGuid():N}");
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Root.SubKey);
    }

    public RegistryPath Root { get; }

    public ClassesRoots Classes => ClassesRoots.ForSingleRoot(Root);

    /// <summary>Creates a key below the sandbox and sets string values on it.</summary>
    public RegistryPath Create(string relativePath, params (string Name, string Value)[] values)
    {
        var path = Root / relativePath;
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path.SubKey);
        foreach (var (name, value) in values)
        {
            key.SetValue(name, value);
        }

        return path;
    }

    public bool Exists(string relativePath) => RegistryStore.Exists(Root / relativePath);

    public bool HasValue(string relativePath, string valueName)
    {
        using var key = RegistryStore.OpenRead(Root / relativePath);
        return key?.GetValueNames().Contains(valueName) == true;
    }

    public void Dispose() => Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Root.SubKey, throwOnMissingSubKey: false);
}
