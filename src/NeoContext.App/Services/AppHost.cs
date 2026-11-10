using System.Globalization;
using Microsoft.Win32;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Dictionaries;
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

        // The dictionary texts follow the app language (settings), otherwise the Windows language.
        var dictionaryCulture = CultureInfo.GetCultureInfo(string.IsNullOrEmpty(Settings.Language) ? CultureInfo.CurrentUICulture.Name : Settings.Language);

        // The shared command pool lives in the machine-wide registry; the custom key is whatever the user chose.
        var commandStore = new RegistryPath(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell");
        var guidInfo = GuidInfoCatalog.LoadEmbedded(dictionaryCulture);
        var verbs = new ShellVerbProvider(classes, resolver, commandStore, () => Settings.CustomRegistryPath);
        var shellExtensions = new ShellExtensionProvider(classes, resolver, guidInfo, () => Settings.CustomRegistryPath);
        FileTypes = new FileTypeScanner(classes, verbs, shellExtensions);
        Creator = new EntryCreator(classes, Path.Combine(AppInfo.DataDirectory, "Scripts"));
        SendTo = new SendToProvider(SendToProvider.DefaultDirectory, resolver);
        OpenWith = new OpenWithProvider(classes, resolver);
        WinX = new WinXProvider(WinXProvider.DefaultRoot, resolver, WinXPathGeneralizer.ForCurrentSystem());

        var enhance = new EnhanceMenuProvider(
            new EnhanceMenuStore(classes, Path.Combine(AppInfo.DataDirectory, "Scripts")),
            resolver,
            () => EnhanceMenus.LoadEmbedded(dictionaryCulture));
        Blocked = BlockedShellExtensions.Default;
        var dropEffect = new DropEffectProvider(classes, Strings.Get);
        var guidBlocked = new GuidBlockedProvider(Blocked, classes, resolver, guidInfo);
        var detailed = new DetailedEditProvider(
            new DetailedEditStore(classes),
            resolver,
            () => DetailedEdit.LoadEmbedded(dictionaryCulture));

        IEntryProvider[] providers =
        [
            verbs,
            shellExtensions,
            new ShellNewProvider(classes, resolver),
            WinX,
            SendTo,
            OpenWith,
            enhance,
            detailed,
            dropEffect,
            guidBlocked,
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

    public SendToProvider SendTo { get; }

    public OpenWithProvider OpenWith { get; }

    public BlockedShellExtensions Blocked { get; }

    public WinXProvider WinX { get; }

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
