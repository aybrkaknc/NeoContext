using System.Runtime.InteropServices;
using NeoContext.Core.Results;

namespace NeoContext.Updates;

/// <summary>Checks a file's Authenticode signature with WinVerifyTrust, without any UI.</summary>
public static partial class AuthenticodeVerifier
{
    /// <summary>WINTRUST_ACTION_GENERIC_VERIFY_V2.</summary>
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;

    public static Result Verify(string path)
    {
        var pathPointer = Marshal.StringToHGlobalUni(Path.GetFullPath(path));
        var fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = pathPointer }, fileInfo, false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = UiNone,
                RevocationChecks = RevokeNone,
                UnionChoice = ChoiceFile,
                File = fileInfo,
                StateAction = StateActionVerify,
            };

            var action = GenericVerifyV2;
            var status = WinVerifyTrust(new IntPtr(-1), ref action, ref data);

            // Always release the verification state, whatever the outcome.
            data.StateAction = StateActionClose;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);

            return status == 0
                ? Result.Success()
                : Result.Failure(ErrorKind.Integrity, $"{Path.GetFileName(path)} is not validly signed (0x{status:X8}).");
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeHGlobal(pathPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WinTrustData data);
}
