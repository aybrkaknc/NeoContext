using System.Runtime.InteropServices;

namespace NeoContext.Shell.Interop;

/// <summary>PROPERTYKEY: a property set id plus a property id.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PropertyKey(Guid FormatId, uint PropertyId)
{
    /// <summary>PKEY_Link_TargetParsingPath: the shortcut's target path.</summary>
    public static readonly PropertyKey LinkTargetParsingPath = new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);

    /// <summary>PKEY_Link_Arguments: the shortcut's command-line arguments.</summary>
    public static readonly PropertyKey LinkArguments = new(new Guid("436F2667-14E2-4FEB-B30A-146C53B5B674"), 100);

    /// <summary>System.Winx.Hash: the hash Explorer requires before it shows a shortcut in the Win+X menu.</summary>
    public static readonly PropertyKey WinXHash = new(new Guid("FB8D2D7B-90D1-4E34-BF60-6EAC09922BBF"), 2);
}

/// <summary>A minimal PROPVARIANT covering the types NeoContext uses (VT_EMPTY, VT_UI4, VT_LPWSTR).</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    public const ushort VtEmpty = 0;
    public const ushort VtUI4 = 19;
    public const ushort VtLpwstr = 31;

    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public uint UInt32Value;
    [FieldOffset(8)] public IntPtr Pointer;

    public static PropVariant FromUInt32(uint value) => new() { VarType = VtUI4, UInt32Value = value };

    public readonly string? AsString() => VarType == VtLpwstr ? Marshal.PtrToStringUni(Pointer) : null;

    public readonly uint? AsUInt32() => VarType == VtUI4 ? UInt32Value : null;
}

/// <summary>IPropertyStore. Methods return HRESULTs so failures are checked, not swallowed.</summary>
[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);

    [PreserveSig] int GetAt(uint index, out PropertyKey key);

    [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);

    [PreserveSig] int SetValue(in PropertyKey key, in PropVariant value);

    [PreserveSig] int Commit();
}

/// <summary>Opens the property store of a file and releases it on dispose.</summary>
internal sealed class FilePropertyStore : IDisposable
{
    private readonly IPropertyStore _store;

    private FilePropertyStore(IPropertyStore store) => _store = store;

    public static FilePropertyStore Open(string path, bool writable)
    {
        var flags = writable ? NativeMethods.GetPropertyStoreFlags.ReadWrite : NativeMethods.GetPropertyStoreFlags.Default;
        var hr = NativeMethods.SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, flags, typeof(IPropertyStore).GUID, out var pointer);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return new FilePropertyStore((IPropertyStore)Marshal.GetObjectForIUnknown(pointer));
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public string? GetString(PropertyKey key)
    {
        Marshal.ThrowExceptionForHR(_store.GetValue(key, out var value));
        try
        {
            return value.AsString();
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
        }
    }

    public uint? GetUInt32(PropertyKey key)
    {
        Marshal.ThrowExceptionForHR(_store.GetValue(key, out var value));
        try
        {
            return value.AsUInt32();
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
        }
    }

    public void SetUInt32(PropertyKey key, uint value)
    {
        Marshal.ThrowExceptionForHR(_store.SetValue(key, PropVariant.FromUInt32(value)));
        Marshal.ThrowExceptionForHR(_store.Commit());
    }

    public void Dispose() => Marshal.ReleaseComObject(_store);
}
