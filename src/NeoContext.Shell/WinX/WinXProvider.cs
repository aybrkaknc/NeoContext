using System.Runtime.InteropServices;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;
using NeoContext.Shell.Interop;

namespace NeoContext.Shell.WinX;

/// <summary>
/// The Win+X menu: <c>%LOCALAPPDATA%\Microsoft\Windows\WinX\GroupN\*.lnk</c>.
/// <para>
/// An entry is hidden with the file's Hidden attribute. Its name is the shortcut description,
/// then the desktop.ini localized name, then the file name. After renaming, the Win+X hash is
/// written again so Explorer keeps showing the shortcut. Explorer reads this menu only at start-up,
/// so changes need an Explorer restart.
/// </para>
/// </summary>
public sealed partial class WinXProvider(string rootDirectory, IIndirectStringResolver resolver, WinXPathGeneralizer generalizer) : IEntryProvider, IEntryMover
{
    private const EntryCapabilities Capabilities =
        EntryCapabilities.Toggle | EntryCapabilities.Rename | EntryCapabilities.Delete | EntryCapabilities.Move;

    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WinX");

    public EntryKind Kind => EntryKind.WinX;

    public bool ChangesRequireExplorerRestart => true;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        if (!Directory.Exists(rootDirectory))
        {
            return Result<IReadOnlyList<ContextMenuEntry>>.Success([]);
        }

        try
        {
            var entries = new List<ContextMenuEntry>();
            foreach (var group in Directory.EnumerateDirectories(rootDirectory).Order(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var file in Directory.EnumerateFiles(group, "*.lnk").Order(StringComparer.OrdinalIgnoreCase))
                {
                    entries.Add(ToEntry(group, file));
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyList<ContextMenuEntry>>.Failure(ErrorKind.Unexpected, $"Could not read the Win+X folder: {ex.Message}");
        }
    }

    private ContextMenuEntry ToEntry(string group, string file)
    {
        string? description = null, target = null, icon = null, program = null;
        try
        {
            using var link = ShellLink.Open(file);
            description = link.Description;
            program = link.TargetPath;
            target = $"{link.TargetPath} {link.Arguments}".Trim();
            icon = link.IconLocation;
        }
        catch (COMException)
        {
            // A broken shortcut is still listed so the user can delete it.
        }

        var name = Clean(description) ?? Clean(DesktopIni.GetLocalizedName(file)) ?? Path.GetFileNameWithoutExtension(file);
        var hidden = File.GetAttributes(file).HasFlag(FileAttributes.Hidden);

        return new ContextMenuEntry(
            Id: file,
            Kind: EntryKind.WinX,
            Section: MenuSection.WinX,
            Key: Path.GetFileName(file),
            DisplayName: name,
            State: hidden ? EntryState.Disabled : EntryState.Enabled,
            Location: file,
            Capabilities: Capabilities)
        {
            Command = string.IsNullOrEmpty(target) ? null : target,
            Description = FileDescriptions.Describe(program),
            Publisher = FileDescriptions.Publisher(program),
            IconLocation = icon,
            Detail = Path.GetFileName(group),
        };
    }

    /// <summary>The group folders (Group1, Group2, ...) in menu order.</summary>
    public IReadOnlyList<string> GroupNames() =>
        Directory.Exists(rootDirectory)
            ? [.. Directory.EnumerateDirectories(rootDirectory).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)]
            : [];

    /// <summary>
    /// Creates <c>NN - name.lnk</c> at the end of a group, with its display name and the Win+X hash
    /// Explorer needs. If any step fails the file is removed again.
    /// </summary>
    public Result CreateShortcut(string group, string name, string target, string? arguments)
    {
        var folder = Path.Combine(rootDirectory, group);
        if (!Directory.Exists(folder))
        {
            return Result.Failure(ErrorKind.NotFound, $"Group not found: {group}");
        }

        if (!File.Exists(target))
        {
            return Result.Failure(ErrorKind.NotFound, $"Not found: {target}");
        }

        var title = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(target) : name.Trim();
        var safeName = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string? file = null;
        try
        {
            var next = Directory.EnumerateFiles(folder, "*.lnk")
                .Select(f => SplitName(Path.GetFileNameWithoutExtension(f)).Number ?? 0)
                .DefaultIfEmpty(0)
                .Max() + 1;
            file = Path.Combine(folder, $"{next:00} - {safeName}.lnk");

            using (var link = ShellLink.Create())
            {
                link.TargetPath = target;
                link.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    link.Arguments = arguments.Trim();
                }

                link.Description = title;
                link.SaveAs(file);
            }

            DesktopIni.SetLocalizedName(file, title);
            WinXHasher.Apply(file, generalizer);
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            RemoveQuietly(file);
            return Result.Failure(ErrorKind.AccessDenied, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or COMException)
        {
            RemoveQuietly(file);
            return Result.Failure(ErrorKind.Unexpected, $"Could not create the shortcut: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves a shortcut one place within its group. Explorer orders a group by file name, so the
    /// whole group is renumbered (<c>01 - ...</c>, <c>02 - ...</c>) with the two items swapped.
    /// </summary>
    public Result Move(ContextMenuEntry entry, int direction) => Guard(entry, () =>
    {
        var folder = Path.GetDirectoryName(entry.Location)!;
        var files = Directory.EnumerateFiles(folder, "*.lnk").Order(StringComparer.OrdinalIgnoreCase).ToList();
        var from = files.FindIndex(f => f.Equals(entry.Location, StringComparison.OrdinalIgnoreCase));
        var to = from + Math.Sign(direction);
        if (from < 0 || to < 0 || to >= files.Count)
        {
            return;
        }

        (files[from], files[to]) = (files[to], files[from]);
        Renumber(folder, files);
    });

    private static void Renumber(string folder, List<string> ordered)
    {
        var moves = ordered
            .Select((file, index) => (Old: file, New: Path.Combine(folder, $"{index + 1:00} - {SplitName(Path.GetFileNameWithoutExtension(file)).Text}.lnk")))
            .Where(m => !m.Old.Equals(m.New, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var names = moves.ToDictionary(m => m.Old, m => DesktopIni.GetLocalizedName(m.Old));

        // Two phases, so two files never share a name halfway through.
        foreach (var move in moves)
        {
            File.Move(move.Old, move.Old + ".neocontext-tmp");
        }

        foreach (var move in moves)
        {
            File.Move(move.Old + ".neocontext-tmp", move.New);
        }

        // The display names are stored by file name, so they move along.
        foreach (var move in moves)
        {
            DesktopIni.RemoveLocalizedName(move.Old);
        }

        foreach (var move in moves)
        {
            if (names[move.Old] is { } name)
            {
                DesktopIni.SetLocalizedName(move.New, name);
            }
        }
    }

    /// <summary>Splits <c>"02 - Run"</c> into the number 2 and the text "Run"; a name without a number keeps all of it.</summary>
    internal static (int? Number, string Text) SplitName(string baseName)
    {
        var match = NumberedName().Match(baseName);
        return match.Success ? ((int?)int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), match.Groups[2].Value) : ((int?)null, baseName);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(\d+)\s*-\s*(.*)$")]
    private static partial System.Text.RegularExpressions.Regex NumberedName();

    /// <summary>The Win+X folder Windows gives every new user. It holds the factory shortcuts, with valid hashes.</summary>
    public static string DefaultUserRoot { get; } = Path.Combine(
        (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + @"\",
        @"Users\Default\AppData\Local\Microsoft\Windows\WinX");

    /// <summary>
    /// Puts the factory Win+X menu back. The current folder is copied to <paramref name="backupDirectory"/>
    /// first, so nothing is lost.
    /// </summary>
    public Result RestoreDefaults(string backupDirectory, string? defaultRoot = null)
    {
        var source = defaultRoot ?? DefaultUserRoot;
        if (!Directory.Exists(source))
        {
            return Result.Failure(ErrorKind.NotFound, $"The default Win+X menu was not found: {source}");
        }

        try
        {
            if (Directory.Exists(rootDirectory))
            {
                CopyDirectory(rootDirectory, Path.Combine(backupDirectory, "WinX", DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", System.Globalization.CultureInfo.InvariantCulture)));
                ClearAttributes(rootDirectory);
                Directory.Delete(rootDirectory, recursive: true);
            }

            CopyDirectory(source, rootDirectory);
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure(ErrorKind.AccessDenied, ex.Message);
        }
        catch (IOException ex)
        {
            return Result.Failure(ErrorKind.Unexpected, $"Could not restore the Win+X menu: {ex.Message}");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        // Attributes last: a read-only folder cannot take new files.
        File.SetAttributes(destination, File.GetAttributes(source));
    }

    private static void ClearAttributes(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        File.SetAttributes(directory, FileAttributes.Normal);
    }

    private static void RemoveQuietly(string? file)
    {
        try
        {
            if (file is not null && File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the half-made file is not hashed, so Explorer ignores it.
        }
    }

    private string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.StartsWith('@') ? resolver.Resolve(text) : text.Trim();
    }

    public Result SetState(ContextMenuEntry entry, EntryState state) => Guard(entry, () =>
    {
        var attributes = File.GetAttributes(entry.Location);
        attributes = state == EntryState.Disabled ? attributes | FileAttributes.Hidden : attributes & ~FileAttributes.Hidden;
        File.SetAttributes(entry.Location, attributes);
    });

    public Result Rename(ContextMenuEntry entry, string newName) => Guard(entry, () =>
    {
        using (var link = ShellLink.Open(entry.Location))
        {
            link.Description = newName;
            link.Save();
        }

        DesktopIni.SetLocalizedName(entry.Location, newName);
        WinXHasher.Apply(entry.Location, generalizer);
    });

    public Result Delete(ContextMenuEntry entry) => Guard(entry, () => RecycleBin.Send(entry.Location));

    private static Result Guard(ContextMenuEntry entry, Action action)
    {
        if (!File.Exists(entry.Location))
        {
            return Result.Failure(ErrorKind.NotFound, $"Shortcut not found: {entry.Location}");
        }

        try
        {
            action();
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure(ErrorKind.AccessDenied, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or COMException)
        {
            return Result.Failure(ErrorKind.Unexpected, $"{entry.Location}: {ex.Message}");
        }
    }
}
