using System.Text;
using NeoContext.Shell.Interop;

namespace NeoContext.Shell;

/// <summary>
/// The <c>[LocalizedFileNames]</c> section of a folder's desktop.ini, which gives files a display
/// name without renaming them. Explorer uses it for the Win+X menu.
/// </summary>
public static class DesktopIni
{
    private const string Section = "LocalizedFileNames";
    private const string FileName = "desktop.ini";

    /// <summary>The display name for <paramref name="filePath"/>, or null when none is set.</summary>
    public static string? GetLocalizedName(string filePath)
    {
        var ini = IniPath(filePath);
        if (!File.Exists(ini))
        {
            return null;
        }

        var buffer = new char[1024];
        var length = NativeMethods.GetPrivateProfileStringW(Section, Path.GetFileName(filePath), null, buffer, (uint)buffer.Length, ini);
        return length == 0 ? null : new string(buffer, 0, (int)length);
    }

    public static void SetLocalizedName(string filePath, string name)
    {
        var ini = IniPath(filePath);
        if (!File.Exists(ini))
        {
            // The profile API writes ANSI to a new file, which loses characters such as "ş".
            // Starting with an empty UTF-16 file makes it keep writing Unicode.
            File.WriteAllText(ini, "", Encoding.Unicode);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        }

        if (!NativeMethods.WritePrivateProfileStringW(Section, Path.GetFileName(filePath), name, ini))
        {
            throw new IOException($"Could not write {ini}.");
        }
    }

    private static string IniPath(string filePath) => Path.Combine(Path.GetDirectoryName(filePath)!, FileName);
}
