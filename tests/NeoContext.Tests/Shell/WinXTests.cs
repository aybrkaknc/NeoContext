using System.Runtime.InteropServices;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Shell;
using NeoContext.Shell.Interop;
using NeoContext.Shell.WinX;

namespace NeoContext.Tests.Shell;

public sealed class WinXTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("neocontext-winx-").FullName;
    private readonly WinXPathGeneralizer _generalizer = WinXPathGeneralizer.ForCurrentSystem();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Generalizer_prefers_the_longest_prefix_and_replaces_only_the_prefix()
    {
        var generalizer = new WinXPathGeneralizer(
        [
            (@"C:\Windows", "{WIN}"),
            (@"C:\Windows\System32", "{SYS}"),
        ]);

        Assert.Equal(@"{SYS}\cmd.exe", generalizer.Generalize(@"C:\Windows\System32\cmd.exe"));
        Assert.Equal(@"{WIN}\explorer.exe", generalizer.Generalize(@"c:\windows\explorer.exe"));
        Assert.Equal(@"{WIN}\x\C:\Windows\y", generalizer.Generalize(@"C:\Windows\x\C:\Windows\y"));
        Assert.Equal(@"D:\Windows\a.exe", generalizer.Generalize(@"D:\Windows\a.exe"));
        Assert.Equal(@"C:\WindowsApps\a.exe", generalizer.Generalize(@"C:\WindowsApps\a.exe"));
    }

    [Fact]
    public void HashData_output_bytes_do_not_depend_on_output_length()
    {
        // This is why a 4-byte hash buffer is enough (the original asked for one byte per input
        // byte, which HashData rejects above 256 bytes and failed silently for long paths).
        var data = new byte[600];
        Random.Shared.NextBytes(data);
        var short4 = new byte[4];
        var long64 = new byte[64];

        Assert.Equal(0, NativeMethods.HashData(data, data.Length, short4, 4));
        Assert.Equal(0, NativeMethods.HashData(data, data.Length, long64, 64));
        Assert.Equal(short4, long64[..4]);
    }

    [Fact]
    public void Hash_ignores_case()
    {
        Assert.Equal(
            WinXHasher.ComputeHash(@"C:\Tools\App.exe", "/X", _generalizer),
            WinXHasher.ComputeHash(@"c:\tools\app.exe", "/x", _generalizer));
    }

    /// <summary>
    /// The reference check: Windows ships Win+X shortcuts hashed by Microsoft. Recomputing their
    /// hash must give the stored value. Skipped (passes) on machines without a Win+X folder.
    /// </summary>
    [Fact]
    public void Computed_hash_matches_the_hash_windows_stored_in_its_own_shortcuts()
    {
        if (!Directory.Exists(WinXProvider.DefaultRoot))
        {
            return;
        }

        var checkedCount = 0;
        foreach (var lnk in Directory.EnumerateFiles(WinXProvider.DefaultRoot, "*.lnk", SearchOption.AllDirectories))
        {
            var stored = WinXHasher.ReadStoredHash(lnk);
            if (stored is null)
            {
                continue;
            }

            Assert.Equal(stored, WinXHasher.ComputeHashForShortcut(lnk, _generalizer));
            checkedCount++;
        }

        Assert.True(checkedCount > 0, "No hashed Win+X shortcut was found to compare against.");
    }

    [Fact]
    public void Provider_lists_toggles_renames_and_rehashes()
    {
        var group = Directory.CreateDirectory(Path.Combine(_root, "Group1")).FullName;
        var lnk = Path.Combine(group, "01 - Notepad.lnk");
        using (var link = ShellLink.Create())
        {
            link.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
            link.Arguments = "/A";
            link.SaveAs(lnk);
        }

        var provider = new WinXProvider(_root, NullIndirectStringResolver.Instance, _generalizer);

        var entry = Assert.Single(provider.Load().Value);
        Assert.Equal("01 - Notepad", entry.DisplayName);
        Assert.Equal("Group1", entry.Detail);
        Assert.EndsWith("notepad.exe /A", entry.Command, StringComparison.OrdinalIgnoreCase);

        Assert.True(provider.SetState(entry, EntryState.Disabled).IsSuccess);
        Assert.True(File.GetAttributes(lnk).HasFlag(FileAttributes.Hidden));
        Assert.Equal(EntryState.Disabled, provider.Load().Value[0].State);

        Assert.True(provider.SetState(entry, EntryState.Enabled).IsSuccess);
        Assert.True(provider.Rename(entry, "Not Defteri – Türkçe").IsSuccess);

        var renamed = Assert.Single(provider.Load().Value);
        Assert.Equal("Not Defteri – Türkçe", renamed.DisplayName);
        Assert.Equal("Not Defteri – Türkçe", DesktopIni.GetLocalizedName(lnk));
        Assert.Equal(WinXHasher.ComputeHashForShortcut(lnk, _generalizer), WinXHasher.ReadStoredHash(lnk));
    }

    [Fact]
    public void Missing_root_gives_no_entries()
    {
        var provider = new WinXProvider(Path.Combine(_root, "nope"), NullIndirectStringResolver.Instance, _generalizer);

        Assert.Empty(provider.Load().Value);
    }

    [Fact]
    public void Indirect_strings_resolve_from_system_dlls()
    {
        var resolver = new IndirectStringResolver();

        // shell32 string 21769 is the "Desktop" folder name on every Windows install.
        Assert.False(string.IsNullOrWhiteSpace(resolver.Resolve(@"@%SystemRoot%\system32\shell32.dll,-21769")));
        Assert.Null(resolver.Resolve("@does-not-exist.dll,-1"));
        _ = Marshal.SizeOf<PropVariant>();
    }
}
