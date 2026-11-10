using System.Reflection;
using NeoContext.Core.Settings;
using NeoContext.Updates;

namespace NeoContext.App.Services;

/// <summary>Facts about this build and where it keeps its files.</summary>
public static class AppInfo
{
    public const string RepositoryOwner = "aybrkaknc";
    public const string RepositoryName = "NeoContext";
    public const string RepositoryUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}";
    public const string OriginalProjectUrl = "https://github.com/BluePointLilac/ContextMenuManager";

    public static SemVersion Version { get; } = SemVersion.TryParse(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        out var version) ? version : new SemVersion(0, 0, 0);

    /// <summary>Settings, logs and backups. A <c>data</c> folder next to the exe makes the app portable.</summary>
    public static string DataDirectory { get; } = AppDataLocation.Resolve(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string OwnershipBackupFile => Path.Combine(DataDirectory, "ownership-backups.json");

    public static string RegistryBackupDirectory => Path.Combine(DataDirectory, "backups");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string DownloadDirectory => Path.Combine(DataDirectory, "downloads");
}
