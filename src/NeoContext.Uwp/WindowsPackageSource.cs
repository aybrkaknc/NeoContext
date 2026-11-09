using Windows.Management.Deployment;

namespace NeoContext.Uwp;

/// <summary>Installed packages for the current user, from the Windows PackageManager. No elevation needed.</summary>
public sealed class WindowsPackageSource : IPackageSource
{
    public IReadOnlyList<InstalledPackage> GetPackages()
    {
        var result = new List<InstalledPackage>();
        foreach (var package in new PackageManager().FindPackagesForUser(""))
        {
            // Frameworks and resource packs never declare context menu verbs.
            if (package.IsFramework || package.IsResourcePackage)
            {
                continue;
            }

            string path;
            string name;
            try
            {
                path = package.InstalledPath;
                name = package.DisplayName;
            }
            catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // Packages that are being installed or removed can throw here; skip them.
                continue;
            }

            result.Add(new InstalledPackage(package.Id.FamilyName, string.IsNullOrWhiteSpace(name) ? package.Id.Name : name, path));
        }

        return result;
    }
}
