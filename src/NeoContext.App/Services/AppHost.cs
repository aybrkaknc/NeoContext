using NeoContext.Core.Abstractions;
using NeoContext.Core.Rules;
using NeoContext.Core.Services;
using NeoContext.Core.Settings;
using NeoContext.Registry;
using NeoContext.Registry.Ownership;
using NeoContext.Registry.Providers;
using NeoContext.Shell;
using NeoContext.Shell.WinX;
using NeoContext.Updates;
using NeoContext.Uwp;

namespace NeoContext.App.Services;

/// <summary>
/// The composition root: every service is created here, once, and handed to whoever needs it.
/// Wiring by hand keeps the dependency graph visible in one short file.
/// </summary>
public sealed class AppHost
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public AppHost()
    {
        SettingsStore = new JsonSettingsStore(AppInfo.SettingsFile);
        Settings = SettingsStore.Load();

        var resolver = new IndirectStringResolver();
        var classes = ClassesRoots.Default;
        Protection = new ProtectionPolicy(Settings.ProtectEssentialEntries);

        var verbs = new ShellVerbProvider(classes, resolver);
        var shellExtensions = new ShellExtensionProvider(classes, resolver);
        FileTypes = new FileTypeScanner(classes, verbs, shellExtensions);
        Creator = new EntryCreator(classes, Path.Combine(AppInfo.DataDirectory, "Scripts"));

        IEntryProvider[] providers =
        [
            verbs,
            shellExtensions,
            new ShellNewProvider(classes, resolver),
            new WinXProvider(WinXProvider.DefaultRoot, resolver, WinXPathGeneralizer.ForCurrentSystem()),
            new PackagedMenuProvider(new WindowsPackageSource(), BlockedShellExtensions.Default),
        ];

        Menus = new ContextMenuService(providers, Protection);
        Ownership = new KeyOwnershipService(AppInfo.OwnershipBackupFile);
        Exporter = new RegistryExporter(AppInfo.RegistryBackupDirectory);
        UpdateChecker = new UpdateChecker(new GitHubReleaseClient(Http, AppInfo.RepositoryOwner, AppInfo.RepositoryName), AppInfo.Version);
        UpdateDownloader = new UpdateDownloader(Http);
    }

    public ISettingsStore SettingsStore { get; }

    /// <summary>The current settings. Replace through <see cref="UpdateSettings"/> so they are saved.</summary>
    public AppSettings Settings { get; private set; }

    public ProtectionPolicy Protection { get; }

    public ContextMenuService Menus { get; }

    public FileTypeScanner FileTypes { get; }

    public EntryCreator Creator { get; }

    public KeyOwnershipService Ownership { get; }

    public RegistryExporter Exporter { get; }

    public UpdateChecker UpdateChecker { get; }

    public UpdateDownloader UpdateDownloader { get; }

    public void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings).Normalize();
        Protection.Enabled = Settings.ProtectEssentialEntries;
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (IOException ex)
        {
            AppLog.Error("Could not save settings.", ex);
        }
    }
}
