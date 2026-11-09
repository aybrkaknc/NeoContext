using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NeoContext.Registry.Ownership;

/// <summary>
/// Enables token privileges for the lifetime of the scope and disables them again on dispose.
/// The token handle is a SafeHandle, so it is always closed (the original leaked one per call).
/// </summary>
internal sealed partial class PrivilegeScope : IDisposable
{
    public const string TakeOwnership = "SeTakeOwnershipPrivilege";
    public const string Restore = "SeRestorePrivilege";

    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    private readonly SafeAccessTokenHandle _token;
    private readonly string[] _privileges;

    private PrivilegeScope(SafeAccessTokenHandle token, string[] privileges)
    {
        _token = token;
        _privileges = privileges;
    }

    public static PrivilegeScope Enable(params string[] privileges)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var scope = new PrivilegeScope(token, privileges);
        try
        {
            foreach (var privilege in privileges)
            {
                scope.Set(privilege, enable: true);
            }
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        return scope;
    }

    public void Dispose()
    {
        foreach (var privilege in _privileges)
        {
            try
            {
                Set(privilege, enable: false);
            }
            catch (Win32Exception)
            {
                // Best effort: the process may not hold the privilege at all.
            }
        }

        _token.Dispose();
    }

    private void Set(string privilege, bool enable)
    {
        if (!LookupPrivilegeValueW(null, privilege, out var luid))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var state = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = enable ? SePrivilegeEnabled : 0 };
        if (!AdjustTokenPrivileges(_token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // AdjustTokenPrivileges "succeeds" with ERROR_NOT_ALL_ASSIGNED when the token lacks the privilege.
        const int ErrorNotAllAssigned = 1300;
        if (Marshal.GetLastPInvokeError() == ErrorNotAllAssigned)
        {
            throw new Win32Exception(ErrorNotAllAssigned, $"The process does not hold {privilege}.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary>TOKEN_PRIVILEGES with exactly one entry.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        SafeAccessTokenHandle tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);
}
