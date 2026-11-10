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
public sealed class WinXProvider(string rootDirectory, IIndirectStringResolver resolver, WinXPathGeneralizer generalizer) : IEntryProvider
{
    private const EntryCapabilities Capabilities = EntryCapabilities.Toggle | EntryCapabilities.Rename | EntryCapabilities.Delete;

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
