using System.Runtime.InteropServices;

namespace NeoContext.App.Services;

/// <summary>Raw pixels of an icon: premultiplied BGRA, top row first.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// Reads one icon out of an .exe, .dll or .icl by index (a negative index is a resource id, as in
/// <c>shell32.dll,-16</c>) and returns its pixels. Runs on any thread.
/// </summary>
internal static partial class IconExtractor
{
    public static IconPixels? Extract(string file, int index)
    {
        var large = new IntPtr[1];
        if (ExtractIconEx(file, index, large, null, 1) < 1 || large[0] == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ToPixels(large[0]);
        }
        finally
        {
            DestroyIcon(large[0]);
        }
    }

    private static IconPixels? ToPixels(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var info))
        {
            return null;
        }

        try
        {
            // Monochrome icons have no colour bitmap; they are rare and not worth special handling.
            if (info.hbmColor == IntPtr.Zero || GetObject(info.hbmColor, Marshal.SizeOf<Bitmap>(), out var bitmap) == 0)
            {
                return null;
            }

            var width = bitmap.bmWidth;
            var height = bitmap.bmHeight;
            var color = ReadBits(info.hbmColor, width, height);
            var mask = ReadBits(info.hbmMask, width, height);
            if (color is null || mask is null)
            {
                return null;
            }

            // Icons with an alpha channel carry it in the colour bitmap; older ones use the mask (black = opaque).
            var hasAlpha = false;
            for (var i = 3; i < color.Length; i += 4)
            {
                if (color[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            for (var i = 0; i < color.Length; i += 4)
            {
                var alpha = hasAlpha ? color[i + 3] : mask[i] == 0 ? (byte)255 : (byte)0;
                color[i] = (byte)(color[i] * alpha / 255);
                color[i + 1] = (byte)(color[i + 1] * alpha / 255);
                color[i + 2] = (byte)(color[i + 2] * alpha / 255);
                color[i + 3] = alpha;
            }

            return new IconPixels(width, height, color);
        }
        finally
        {
            DeleteObject(info.hbmColor);
            DeleteObject(info.hbmMask);
        }
    }

    private static byte[]? ReadBits(IntPtr bitmap, int width, int height)
    {
        var header = new BitmapInfoHeader
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height, // negative: top row first
            biPlanes = 1,
            biBitCount = 32,
        };

        var data = new byte[width * height * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(dc, bitmap, 0, (uint)height, data, ref header, 0) == 0 ? null : data;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

#pragma warning disable SYSLIB1054 // DllImport is fine here: the signatures use structs and arrays.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, out Bitmap bitmap);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
#pragma warning restore SYSLIB1054
}
