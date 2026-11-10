using System.Runtime.InteropServices;

namespace NeoContext.App.Views;

/// <summary>
/// Keeps a window from being resized below a fixed size. The window presenter's minimum is not always
/// enforced, so this answers WM_GETMINMAXINFO directly. The limits are in device-independent pixels.
/// </summary>
internal static class WindowMinimumSize
{
    public const int MinWidth = 720;
    public const int MinHeight = 480;

    private const uint WmGetMinMaxInfo = 0x0024;
    private const nuint SubclassId = 1;

    // Kept in a field so the garbage collector does not free the callback while the window exists.
    private static SubclassProc? _proc;

    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint subclassId, nuint refData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint subclassId, nuint refData);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    /// <summary>Installs the limit on the window with the given handle.</summary>
    public static void Install(nint hWnd)
    {
        _proc = Handle;
        SetWindowSubclass(hWnd, _proc, SubclassId, 0);
    }

    private static nint Handle(nint hWnd, uint msg, nint wParam, nint lParam, nuint subclassId, nuint refData)
    {
        if (msg == WmGetMinMaxInfo && lParam != 0)
        {
            // The limits are in pixels, so scale them by the window's DPI (96 is 100 %).
            var scale = GetDpiForWindow(hWnd) / 96.0;
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize = new Point
            {
                X = (int)Math.Round(MinWidth * scale),
                Y = (int)Math.Round(MinHeight * scale),
            };
            Marshal.StructureToPtr(info, lParam, false);
            return 0;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
