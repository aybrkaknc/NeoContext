using Microsoft.Win32;
using NeoContext.Core.Models;
using NeoContext.Registry.Providers;

namespace NeoContext.Registry;

/// <summary>
/// Finds the context menu entries that belong to specific file extensions. For each extension it
/// looks at three places, each with a <c>shell</c> and a <c>shellex</c> branch:
/// <c>SystemFileAssociations\.ext</c>, the extension's ProgID (the user's choice first), and the
/// class of its perceived type (<c>SystemFileAssociations\video</c>, ...).
/// Entries use the normal verb and extension kinds, so every existing action works on them.
/// </summary>
public sealed class FileTypeScanner(
    ClassesRoots roots,
    ShellVerbProvider verbs,
    ShellExtensionProvider extensions,
    RegistryPath? fileExtsRoot = null)
{
    private static readonly RegistryPath DefaultFileExts =
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts");

    private readonly RegistryPath _fileExts = fileExtsRoot ?? DefaultFileExts;

    /// <summary>
    /// Entries for the given extensions (such as <c>.mp4</c>). A class shared by several extensions
    /// (one ProgID, or a perceived type) is read once, and its entries list every extension it came from.
    /// </summary>
    public IReadOnlyList<ContextMenuEntry> Scan(IEnumerable<string> extensionNames)
    {
        var sources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in extensionNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var classKey in ClassKeys(extension))
            {
                if (!sources.TryGetValue(classKey, out var list))
                {
                    sources[classKey] = list = [];
                }

                list.Add(extension.TrimStart('.'));
            }
        }

        var result = new List<ContextMenuEntry>();
        foreach (var (classKey, labels) in sources)
        {
            var origin = string.Join(", ", labels);
            foreach (var entry in verbs.LoadClass(MenuSection.FileTypes, classKey).Concat(extensions.LoadClass(MenuSection.FileTypes, classKey)))
            {
                result.Add(entry with { Detail = $"{origin} · {entry.Detail}" });
            }
        }

        return result;
    }

    /// <summary>The perceived types Windows knows, as the names of their <c>SystemFileAssociations</c> classes.</summary>
    public static IReadOnlyList<string> PerceivedTypes { get; } = ["Text", "Document", "Image", "Video", "Audio", "Compressed", "System"];

    /// <summary>The folder types with their own menu, as <c>SystemFileAssociations\Directory.&lt;type&gt;</c>.</summary>
    public static IReadOnlyList<string> FolderTypes { get; } = ["Document", "Image", "Video", "Audio"];

    /// <summary>Entries of one class, shown on the page of <paramref name="section"/>.</summary>
    public IReadOnlyList<ContextMenuEntry> ScanClass(MenuSection section, string classKey, string label) =>
        [.. verbs.LoadClass(section, classKey).Concat(extensions.LoadClass(section, classKey)).Select(e => e with { Detail = $"{label} · {e.Detail}" })];

    /// <summary>
    /// Every entry that applies to a file or folder: the classes Explorer reads for it, in the order it reads them.
    /// A file without any associated program also gets the <c>Unknown</c> class. A missing path gives nothing.
    /// </summary>
    public IReadOnlyList<ContextMenuEntry> Analyze(string path)
    {
        List<string> classKeys = [];
        if (Directory.Exists(path))
        {
            classKeys.Add("Folder");
            if (string.Equals(Path.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase))
            {
                classKeys.Add("Drive");
            }
            else
            {
                classKeys.AddRange(["Directory", "AllFilesystemObjects"]);
            }
        }
        else if (File.Exists(path))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension.Length == 0)
            {
                extension = ".";
            }

            classKeys.AddRange(["*", "AllFilesystemObjects"]);
            classKeys.AddRange(ClassKeys(extension));
            if (ProgIds(extension).Count == 0)
            {
                classKeys.Add("Unknown");
            }
        }

        return [.. classKeys.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(key => ScanClass(MenuSection.Analysis, key, key))];
    }

    /// <summary>The perceived type of an extension as Explorer sees it, or null when it has none.</summary>
    public string? GetPerceivedType(string extension) =>
        RegistryStore.GetString(roots.Merged / extension, "PerceivedType") is { Length: > 0 } type ? type.Trim() : null;

    /// <summary>
    /// Sets the perceived type of an extension for this user (null removes the per-user value). A machine-wide
    /// value still applies when there is no per-user one.
    /// </summary>
    public Core.Results.Result SetPerceivedType(string extension, string? type)
    {
        var path = roots.Writable[0].Path / extension;
        if (type is null)
        {
            var removed = RegistryStore.DeleteValues(path, "PerceivedType");
            return removed.Error?.Kind == Core.Results.ErrorKind.NotFound ? Core.Results.Result.Success() : removed;
        }

        return RegistryStore.SetString(path, "PerceivedType", type.ToLowerInvariant(), create: true);
    }

    /// <summary>Every extension registered on this PC, for suggestions in the picker.</summary>
    public IReadOnlyList<string> KnownExtensions()
    {
        using var key = RegistryStore.OpenRead(roots.Merged);
        return key is null
            ? []
            : [.. key.GetSubKeyNames().Where(n => n.StartsWith('.')).Order(StringComparer.OrdinalIgnoreCase)];
    }

    private IEnumerable<string> ClassKeys(string extension)
    {
        yield return $@"SystemFileAssociations\{extension}";

        foreach (var progId in ProgIds(extension))
        {
            yield return progId;
        }

        var perceived = RegistryStore.GetString(roots.Merged / extension, "PerceivedType")
                        ?? RegistryStore.GetString(roots.Merged / "SystemFileAssociations" / extension, "PerceivedType");
        if (!string.IsNullOrWhiteSpace(perceived))
        {
            yield return $@"SystemFileAssociations\{perceived.Trim()}";
        }
    }

    /// <summary>The user's chosen handler first, then the machine and user defaults. "Applications\..." is skipped.</summary>
    private List<string> ProgIds(string extension)
    {
        var found = new List<string>();
        void Add(string? progId)
        {
            if (string.IsNullOrWhiteSpace(progId)
                || progId.StartsWith(@"Applications\", StringComparison.OrdinalIgnoreCase)
                || found.Contains(progId, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            using var key = RegistryStore.OpenRead(roots.Merged / progId);
            if (key is not null)
            {
                found.Add(progId);
            }
        }

        Add(RegistryStore.GetString(_fileExts / extension / "UserChoice", "ProgId"));
        foreach (var root in roots.Writable)
        {
            Add(RegistryStore.GetString(root.Path / extension, ""));
        }

        return found;
    }
}
