using Microsoft.Win32;

namespace NeoContext.Registry;

/// <summary>
/// Finds the icon of the installed program that owns a file. Installers list themselves under the
/// "Uninstall" keys with an install folder and a display icon, so a shell extension DLL inside
/// <c>C:\Program Files\7-Zip</c> can borrow 7-Zip's icon.
/// </summary>
public static class InstalledApps
{
    private static readonly string[] UninstallKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    private static readonly Lazy<IReadOnlyList<(string Folder, string Icon)>> Apps = new(Load);

    /// <summary>The display icon (<c>"path,index"</c>) of the app installed around <paramref name="filePath"/>, or null.</summary>
    public static string? FindIcon(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var path = Environment.ExpandEnvironmentVariables(filePath).Trim('"');
        return Path.IsPathFullyQualified(path)
            ? Apps.Value.FirstOrDefault(a => path.StartsWith(a.Folder, StringComparison.OrdinalIgnoreCase)).Icon
            : null;
    }

    private static List<(string Folder, string Icon)> Load()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var apps = new List<(string Folder, string Icon)>();

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var subKey in UninstallKeys)
            {
                using var baseKey = RegistryStore.OpenRead(new RegistryPath(hive, subKey));
                if (baseKey is null)
                {
                    continue;
                }

                foreach (var name in baseKey.GetSubKeyNames())
                {
                    using var app = baseKey.OpenSubKey(name);
                    var icon = (app?.GetValue("DisplayIcon") as string)?.Trim();
                    if (string.IsNullOrEmpty(icon))
                    {
                        continue;
                    }

                    // The install folder, or the folder of the icon file when the installer did not record one.
                    var folder = (app!.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                    if (string.IsNullOrEmpty(folder))
                    {
                        var file = Environment.ExpandEnvironmentVariables(icon).Split(',')[0].Trim('"', ' ');
                        folder = Path.IsPathFullyQualified(file) ? Path.GetDirectoryName(file) : null;
                    }

                    // Shared folders (Windows, Program Files itself) would match unrelated files.
                    if (folder is null || !Path.IsPathFullyQualified(folder) || folder.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
                        || folder.Count(c => c == '\\') < 2)
                    {
                        continue;
                    }

                    apps.Add((folder.TrimEnd('\\') + "\\", icon));
                }
            }
        }

        // The most specific folder wins.
        return [.. apps.OrderByDescending(a => a.Folder.Length)];
    }
}
