using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Shell;
using NeoContext.Shell.Interop;

namespace NeoContext.Tests.Shell;

/// <summary>The Send to folder: listing, hiding, renaming and creating shortcuts in a temporary folder.</summary>
public sealed class SendToTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"NeoContext.Tests.{Guid.NewGuid():N}");

    public SendToTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private SendToProvider Provider() => new(_folder, NullIndirectStringResolver.Instance);

    private string File_(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private ContextMenuEntry Load(string fileName)
    {
        var result = Provider().Load();
        Assert.True(result.IsSuccess, result.Error?.Message);
        return Assert.Single(result.Value, e => e.Key == fileName);
    }

    [Fact]
    public void Files_are_listed_without_desktop_ini_and_named_after_the_file()
    {
        File_("Documents.mydocs");
        File_("desktop.ini");

        var entries = Provider().Load().Value;

        var entry = Assert.Single(entries);
        Assert.Equal("Documents", entry.DisplayName);
        Assert.Equal(MenuSection.SendTo, entry.Section);
        Assert.Equal(EntryKind.SendTo, entry.Kind);
        Assert.Equal(EntryState.Enabled, entry.State);
    }

    [Fact]
    public void Hiding_and_showing_toggle_the_Hidden_attribute()
    {
        var path = File_("Tool.lnk.txt");
        var provider = Provider();

        Assert.True(provider.SetState(Load("Tool.lnk.txt"), EntryState.Disabled).IsSuccess);
        Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
        Assert.Equal(EntryState.Disabled, Load("Tool.lnk.txt").State);

        Assert.True(provider.SetState(Load("Tool.lnk.txt"), EntryState.Enabled).IsSuccess);
        Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void Renaming_writes_a_localized_name_and_keeps_the_file()
    {
        var path = File_("Zip.zfsendtotarget");

        Assert.True(Provider().Rename(Load("Zip.zfsendtotarget"), "Sıkıştır").IsSuccess);

        Assert.True(File.Exists(path));
        Assert.Equal("Sıkıştır", Load("Zip.zfsendtotarget").DisplayName);
    }

    [Fact]
    public void A_change_to_a_missing_file_is_reported_as_not_found()
    {
        var path = File_("Gone.dat");
        var entry = Load("Gone.dat");
        File.Delete(path);

        Assert.Equal(ErrorKind.NotFound, Provider().SetState(entry, EntryState.Disabled).Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, Provider().Rename(entry, "x").Error!.Kind);
    }

    [Fact]
    public void A_created_shortcut_points_to_the_target_and_a_repeat_gets_a_number()
    {
        var target = File_("app.exe");

        Assert.True(Provider().CreateShortcut("My app", target, "-x").IsSuccess);
        Assert.True(Provider().CreateShortcut("My app", target, null).IsSuccess);

        var first = Path.Combine(_folder, "My app.lnk");
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(Path.Combine(_folder, "My app (2).lnk")));

        using var link = ShellLink.Open(first);
        Assert.Equal(target, link.TargetPath);
        Assert.Equal("-x", link.Arguments);
        Assert.Equal("My app", Load("My app.lnk").DisplayName);
    }

    [Fact]
    public void A_shortcut_to_something_that_does_not_exist_is_refused()
    {
        var result = Provider().CreateShortcut("Nope", Path.Combine(_folder, "missing.exe"), null);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
        Assert.Empty(Directory.EnumerateFiles(_folder, "*.lnk"));
    }
}
