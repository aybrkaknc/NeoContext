using System.ComponentModel;
using System.Diagnostics;
using NeoContext.Registry;
using Windows.ApplicationModel.DataTransfer;

namespace NeoContext.App.Services;

/// <summary>Actions that leave the app: other processes, the clipboard, the browser.</summary>
public static class SystemActions
{
    public static bool IsElevated => RegistryStore.IsElevated;

    /// <summary>Starts a new elevated instance. Returns false when the user cancels the UAC prompt.</summary>
    public static bool RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user said no to UAC.
            return false;
        }
    }

    /// <summary>Opens Registry Editor at <paramref name="path"/> (it remembers the last key it showed).</summary>
    public static void OpenInRegistryEditor(RegistryPath path)
    {
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
        {
            key.SetValue("LastKey", $@"Computer\{path}");
        }

        Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
    }

    /// <summary>Opens Explorer with the file or folder selected.</summary>
    public static void ShowInExplorer(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(File.Exists(path) ? $"/select,{path}" : path);
        Process.Start(start);
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>The text on the clipboard, or null when there is none.</summary>
    public static async Task<string?> ReadClipboardTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            return content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
