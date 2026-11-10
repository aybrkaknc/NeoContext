using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace NeoContext.Shell.Interop;

/// <summary>Reads and writes .lnk shortcut files through the shell's IShellLink object.</summary>
public sealed class ShellLink : IDisposable
{
    private const int MaxPath = 1024;
    private const int StgmReadWrite = 0x2;

    private readonly IShellLinkW _link;
    private readonly string? _path;

    private ShellLink(string? path)
    {
        _link = (IShellLinkW)new CShellLink();
        _path = path;
        if (path is not null)
        {
            ((IPersistFile)_link).Load(path, StgmReadWrite);
        }
    }

    /// <summary>Opens an existing shortcut.</summary>
    public static ShellLink Open(string path) => new(path);

    /// <summary>Starts a new, empty shortcut. Call <see cref="SaveAs"/> to write it.</summary>
    public static ShellLink Create() => new(null);

    public string TargetPath
    {
        get
        {
            var buffer = new StringBuilder(MaxPath);
            _link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            return buffer.ToString();
        }
        set => _link.SetPath(value);
    }

    public string Arguments
    {
        get
        {
            var buffer = new StringBuilder(MaxPath);
            _link.GetArguments(buffer, buffer.Capacity);
            return buffer.ToString();
        }
        set => _link.SetArguments(value);
    }

    public string Description
    {
        get
        {
            var buffer = new StringBuilder(MaxPath);
            _link.GetDescription(buffer, buffer.Capacity);
            return buffer.ToString();
        }
        set => _link.SetDescription(value);
    }

    /// <summary>The folder the program starts in. Only needed when creating a shortcut.</summary>
    public string WorkingDirectory
    {
        set => _link.SetWorkingDirectory(value);
    }

    /// <summary>The icon as <c>path,index</c>, or null when the shortcut uses its target's icon.</summary>
    public string? IconLocation
    {
        get
        {
            var buffer = new StringBuilder(MaxPath);
            _link.GetIconLocation(buffer, buffer.Capacity, out var index);
            return buffer.Length == 0 ? null : $"{buffer},{index}";
        }
    }

    /// <summary>Saves back to the file the shortcut was opened from.</summary>
    public void Save() => ((IPersistFile)_link).Save(_path ?? throw new InvalidOperationException("Use SaveAs for new shortcuts."), true);

    public void SaveAs(string path) => ((IPersistFile)_link).Save(path, true);

    public void Dispose() => Marshal.ReleaseComObject(_link);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        void Resolve(IntPtr hwnd, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
