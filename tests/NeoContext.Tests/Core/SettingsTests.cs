using NeoContext.Core.Settings;

namespace NeoContext.Tests.Core;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("neocontext-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var store = new JsonSettingsStore(Path.Combine(_dir, "settings.json"));

        Assert.Equal(AppSettings.Default, store.Load());
    }

    [Fact]
    public void Saved_settings_round_trip()
    {
        var store = new JsonSettingsStore(Path.Combine(_dir, "nested", "settings.json"));
        var settings = AppSettings.Default with { Language = "tr-TR", CheckForUpdates = false, UpdateCheckIntervalDays = 14 };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_keeps_a_backup()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal(AppSettings.Default, loaded);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1000)]
    public void Out_of_range_interval_falls_back_to_default(int days)
    {
        var normalized = (AppSettings.Default with { UpdateCheckIntervalDays = days }).Normalize();

        Assert.Equal(AppSettings.Default.UpdateCheckIntervalDays, normalized.UpdateCheckIntervalDays);
    }

    [Fact]
    public void Data_folder_next_to_the_exe_turns_on_portable_mode()
    {
        var appDir = Path.Combine(_dir, "app");
        Directory.CreateDirectory(Path.Combine(appDir, AppDataLocation.PortableFolderName));

        Assert.Equal(Path.Combine(appDir, "data"), AppDataLocation.Resolve(appDir, @"C:\Local"));
        Assert.Equal(Path.Combine(@"C:\Local", "NeoContext"), AppDataLocation.Resolve(_dir, @"C:\Local"));
    }
}
