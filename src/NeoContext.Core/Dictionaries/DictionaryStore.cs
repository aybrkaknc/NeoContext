using System.Reflection;

namespace NeoContext.Core.Dictionaries;

/// <summary>
/// Finds a dictionary file. Order: the user's own copy (<c>user</c>), the copy downloaded from the web (<c>web</c>),
/// then the one built into the app, which is always there.
/// </summary>
public sealed class DictionaryStore(string? directory)
{
    public static readonly string[] FileNames = ["EnhanceMenusDic.xml", "DetailedEditDic.xml", "GuidInfosDic.ini"];

    public static DictionaryStore EmbeddedOnly { get; } = new(null);

    public string? WebDirectory => directory is null ? null : Path.Combine(directory, "web");

    public Stream Open(string fileName)
    {
        if (directory is not null)
        {
            foreach (var folder in new[] { "user", "web" })
            {
                var path = Path.Combine(directory, folder, fileName);
                if (File.Exists(path))
                {
                    try
                    {
                        return File.OpenRead(path);
                    }
                    catch (IOException)
                    {
                        // Locked or being replaced: use the next source.
                    }
                }
            }
        }

        return OpenEmbedded(fileName);
    }

    public static Stream OpenEmbedded(string fileName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"NeoContext.Dictionaries.{fileName}")
        ?? throw new InvalidOperationException($"The embedded dictionary {fileName} is missing.");

    /// <summary>
    /// Downloaded copies belong to the app version that fetched them. After an app update the built-in files are
    /// newer than the old downloads, so the downloads are dropped and fetched again when due.
    /// </summary>
    public void ResetWebIfAppChanged(string appVersion)
    {
        if (WebDirectory is not { } web)
        {
            return;
        }

        var stamp = Path.Combine(web, "app-version.txt");
        try
        {
            if (File.Exists(stamp) && File.ReadAllText(stamp) == appVersion)
            {
                return;
            }

            if (Directory.Exists(web))
            {
                Directory.Delete(web, recursive: true);
            }

            Directory.CreateDirectory(web);
            File.WriteAllText(stamp, appVersion);
        }
        catch (IOException)
        {
            // The built-in files still work; try again next start.
        }
    }
}
