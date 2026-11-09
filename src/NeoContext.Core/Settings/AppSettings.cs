using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoContext.Core.Settings;

/// <summary>
/// User settings. Immutable: change one with a <c>with</c> expression and save the new instance.
/// Every default lives here, in one place.
/// </summary>
public sealed record AppSettings
{
    public static readonly AppSettings Default = new();

    /// <summary>UI language as a BCP-47 tag (e.g. "en-US"), or empty to follow Windows.</summary>
    public string Language { get; init; } = "";

    /// <summary>Refuse to disable or delete entries that break basic Explorer behaviour.</summary>
    public bool ProtectEssentialEntries { get; init; } = true;

    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Days between automatic update checks. Values outside 1..365 fall back to the default.</summary>
    public int UpdateCheckIntervalDays { get; init; } = 7;

    /// <summary>Time of the last successful update check (UTC). Failed checks do not update it.</summary>
    public DateTimeOffset? LastUpdateCheck { get; init; }

    /// <summary>Returns a copy with out-of-range values replaced by their defaults.</summary>
    public AppSettings Normalize() =>
        UpdateCheckIntervalDays is < 1 or > 365
            ? this with { UpdateCheckIntervalDays = Default.UpdateCheckIntervalDays }
            : this;
}

/// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>Stores settings as indented JSON in a single file.</summary>
public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string FilePath { get; } = filePath;

    /// <summary>
    /// Returns the stored settings, or the defaults when the file is missing or unreadable.
    /// A corrupt file is kept as <c>.bak</c> so the user's data is not silently lost.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return AppSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            return AppSettings.Default;
        }
    }

    /// <summary>Writes to a temporary file first and then replaces the old file, so a crash cannot leave half a file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, Options));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}

/// <summary>Decides where settings and backups are stored.</summary>
public static class AppDataLocation
{
    /// <summary>The marker folder that switches the app into portable mode.</summary>
    public const string PortableFolderName = "data";

    /// <summary>
    /// Portable mode: if a <c>data</c> folder exists next to the executable, use it.
    /// Otherwise use <c>%LOCALAPPDATA%\NeoContext</c>.
    /// </summary>
    public static string Resolve(string appDirectory, string localAppData)
    {
        var portable = Path.Combine(appDirectory, PortableFolderName);
        return Directory.Exists(portable) ? portable : Path.Combine(localAppData, "NeoContext");
    }
}
