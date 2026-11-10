using System.Runtime.InteropServices;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;
using NeoContext.Shell.Interop;

namespace NeoContext.Shell;

/// <summary>
/// The "Send to" menu: the files in <c>%AppData%\Microsoft\Windows\SendTo</c>.
/// <para>
/// An entry is hidden with the file's Hidden attribute (Explorer skips hidden files here). Its name
/// is the localized name from desktop.ini, then the file name without extension. Renaming writes
/// the desktop.ini name and leaves the file alone; deleting sends the file to the Recycle Bin.
/// </para>
/// </summary>
public sealed class SendToProvider(string directory, IIndirectStringResolver resolver) : IEntryProvider
{
    private const EntryCapabilities Capabilities = EntryCapabilities.Toggle | EntryCapabilities.Rename | EntryCapabilities.Delete;

    public static string DefaultDirectory { get; } = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);

    public EntryKind Kind => EntryKind.SendTo;

    /// <summary>Explorer reads the folder each time the menu opens.</summary>
    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        if (!Directory.Exists(directory))
        {
            return Result<IReadOnlyList<ContextMenuEntry>>.Success([]);
        }

        try
        {
            var files = Directory.EnumerateFiles(directory)
                .Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase);
            return files.Select(ToEntry).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyList<ContextMenuEntry>>.Failure(ErrorKind.Unexpected, $"Could not read the Send to folder: {ex.Message}");
        }
    }

    private ContextMenuEntry ToEntry(string file)
    {
        string? target = null, program = null, icon = null;
        if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var link = ShellLink.Open(file);
                program = link.TargetPath;
                target = $"{link.TargetPath} {link.Arguments}".Trim();
                icon = link.IconLocation;
            }
            catch (COMException)
            {
                // A broken shortcut is still listed so the user can delete it.
            }
        }

        var localized = DesktopIni.GetLocalizedName(file);
        var name = (localized?.StartsWith('@') == true ? resolver.Resolve(localized) : localized)
                   ?? Path.GetFileNameWithoutExtension(file);
        var hidden = File.GetAttributes(file).HasFlag(FileAttributes.Hidden);

        return new ContextMenuEntry(
            Id: file,
            Kind: EntryKind.SendTo,
            Section: MenuSection.SendTo,
            Key: Path.GetFileName(file),
            DisplayName: name,
            State: hidden ? EntryState.Disabled : EntryState.Enabled,
            Location: file,
            Capabilities: Capabilities)
        {
            Command = string.IsNullOrEmpty(target) ? null : target,
            IconLocation = icon,
            Description = FileDescriptions.Describe(program),
            Publisher = FileDescriptions.Publisher(program),
        };
    }

    public Result SetState(ContextMenuEntry entry, EntryState state) => Guard(entry, () =>
    {
        var attributes = File.GetAttributes(entry.Location);
        attributes = state == EntryState.Disabled ? attributes | FileAttributes.Hidden : attributes & ~FileAttributes.Hidden;
        File.SetAttributes(entry.Location, attributes);
    });

    public Result Rename(ContextMenuEntry entry, string newName) => Guard(entry, () => DesktopIni.SetLocalizedName(entry.Location, newName));

    public Result Delete(ContextMenuEntry entry) => Guard(entry, () => RecycleBin.Send(entry.Location));

    /// <summary>Creates a shortcut to a program, file or folder in the Send to folder.</summary>
    public Result CreateShortcut(string name, string target, string? arguments)
    {
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return Result.Failure(ErrorKind.NotFound, $"Not found: {target}");
        }

        try
        {
            Directory.CreateDirectory(directory);
            var fileName = string.Concat((string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(target) : name.Trim())
                .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var file = Path.Combine(directory, $"{fileName}.lnk");
            for (var number = 2; File.Exists(file); number++)
            {
                file = Path.Combine(directory, $"{fileName} ({number}).lnk");
            }

            using var link = ShellLink.Create();
            link.TargetPath = target;
            link.WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target) ?? "";
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                link.Arguments = arguments.Trim();
            }

            link.SaveAs(file);
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure(ErrorKind.AccessDenied, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or COMException)
        {
            return Result.Failure(ErrorKind.Unexpected, $"Could not create the shortcut: {ex.Message}");
        }
    }

    private static Result Guard(ContextMenuEntry entry, Action action)
    {
        if (!File.Exists(entry.Location))
        {
            return Result.Failure(ErrorKind.NotFound, $"File not found: {entry.Location}");
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
