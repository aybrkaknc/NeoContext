using System.Runtime.InteropServices;

namespace NeoContext.Shell.Interop;

/// <summary>Flat Win32 functions used by the shell layer. Every caller checks the HRESULT.</summary>
internal static partial class NativeMethods
{
    public const int SOk = 0;

    /// <summary>Loads text from an indirect string such as <c>@shell32.dll,-8496</c>.</summary>
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHLoadIndirectString(string source, [Out] char[] outBuffer, uint outBufferLength, IntPtr reserved);

    /// <summary>The shell's data hash, used for the Win+X shortcut hash. Each output byte is independent of the output length.</summary>
    [LibraryImport("shlwapi.dll")]
    public static partial int HashData(byte[] data, int dataLength, [Out] byte[] hash, int hashLength);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, GetPropertyStoreFlags flags, in Guid riid, out IntPtr propertyStore);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant variant);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WritePrivateProfileStringW(string section, string? key, string? value, string fileName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetPrivateProfileStringW(string section, string key, string? defaultValue, [Out] char[] buffer, uint size, string fileName);

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    public static partial int SHFileOperation(ref ShFileOperation operation);

    [Flags]
    public enum GetPropertyStoreFlags
    {
        Default = 0,
        ReadWrite = 0x2,
    }

    /// <summary>SHFILEOPSTRUCTW. Strings are double-null-terminated lists.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ShFileOperation
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }
}
