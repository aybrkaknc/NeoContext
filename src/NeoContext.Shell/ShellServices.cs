using System.Diagnostics;
using System.Runtime.InteropServices;
using NeoContext.Core.Abstractions;
using NeoContext.Shell.Interop;

namespace NeoContext.Shell;

/// <summary>Resolves <c>@dll,-id</c> strings with SHLoadIndirectString and caches the results.</summary>
public sealed class IndirectStringResolver : IIndirectStringResolver
{
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public string? Resolve(string indirectString)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(indirectString, out var cached))
            {
                return cached;
            }

            var buffer = new char[1024];
            var hr = NativeMethods.SHLoadIndirectString(indirectString, buffer, (uint)buffer.Length, IntPtr.Zero);
            string? text = null;
            if (hr == NativeMethods.SOk)
            {
                var end = Array.IndexOf(buffer, '\0');
                text = new string(buffer, 0, end < 0 ? buffer.Length : end);
            }

            text = string.IsNullOrWhiteSpace(text) ? null : text;
            _cache[indirectString] = text;
            return text;
        }
    }
}

/// <summary>Moves files to the Recycle Bin instead of deleting them permanently.</summary>
public static class RecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    public static void Send(string path)
    {
        // pFrom is a list of paths, each ending in \0, with an extra \0 at the end.
        var from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + "\0\0");
        try
        {
            var operation = new NativeMethods.ShFileOperation
            {
                Func = FoDelete,
                From = from,
                Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
            };

            var code = NativeMethods.SHFileOperation(ref operation);
            if (code != 0 || operation.AnyOperationsAborted != 0)
            {
                throw new IOException($"Could not move '{path}' to the Recycle Bin (code {code}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }
}

/// <summary>Restarts Explorer so it reloads cached menus such as Win+X.</summary>
public static class ExplorerRestarter
{
    public static void Restart()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }

        // Windows usually restarts the shell itself; start it if it did not.
        Thread.Sleep(1500);
        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
    }
}
