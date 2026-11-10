using Microsoft.Win32;
using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;

namespace NeoContext.Registry.Providers;

/// <summary>
/// The "Open with" menu: apps registered under <c>Applications\&lt;exe&gt;</c> (only names with a dot, as
/// Explorer lists them), one entry per app.
/// <para>
/// An app is hidden with the <c>NoOpenWith</c> value. Its name is <c>FriendlyAppName</c>, then the
/// program's file description, then the exe name. Deleting removes the app's key; the registry backup
/// made before every delete covers it.
/// </para>
/// </summary>
public sealed class OpenWithProvider(ClassesRoots roots, IIndirectStringResolver resolver) : IEntryProvider
{
    internal const string NoOpenWith = "NoOpenWith";
    internal const string FriendlyAppName = "FriendlyAppName";

    private const EntryCapabilities Capabilities = EntryCapabilities.Toggle | EntryCapabilities.Rename | EntryCapabilities.Delete;

    public EntryKind Kind => EntryKind.OpenWith;

    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var root in roots.Writable)
        {
            var applications = root.Path / "Applications";
            using var key = RegistryStore.OpenRead(applications);
            if (key is null)
            {
                continue;
            }

            foreach (var app in key.GetSubKeyNames().Where(n => n.Contains('.')))
            {
                using var appKey = key.OpenSubKey(app);
                var command = appKey is null ? null : ReadCommand(appKey);
                if (appKey is null || command is null)
                {
                    continue;
                }

                var path = applications / app;
                var description = FileDescriptions.Describe(command);
                entries.Add(new ContextMenuEntry(
                    Id: path.ToString(),
                    Kind: EntryKind.OpenWith,
                    Section: MenuSection.OpenWith,
                    Key: app,
                    DisplayName: DisplayText.Clean(appKey.GetValue(FriendlyAppName) as string, resolver) ?? description ?? app,
                    State: appKey.GetValueNames().Contains(NoOpenWith, StringComparer.OrdinalIgnoreCase) ? EntryState.Disabled : EntryState.Enabled,
                    Location: path.ToString(),
                    Capabilities: Capabilities)
                {
                    Command = command,
                    Description = description,
                    Publisher = FileDescriptions.Publisher(command),
                    Detail = root.Label,
                });
            }
        }

        return entries;
    }

    /// <summary>The command of the "open" verb, otherwise of the first verb that has one. Verbs marked NeverDefault are skipped.</summary>
    private static string? ReadCommand(RegistryKey appKey)
    {
        using var shell = appKey.OpenSubKey("shell");
        if (shell is null)
        {
            return null;
        }

        foreach (var verb in shell.GetSubKeyNames().OrderBy(v => v.Equals("open", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            using var verbKey = shell.OpenSubKey(verb);
            if (verbKey is null || verbKey.GetValue("NeverDefault") is not null)
            {
                continue;
            }

            using var command = verbKey.OpenSubKey("command");
            if (command?.GetValue("") is string { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        var path = RegistryPath.Parse(entry.Location);
        return state == EntryState.Disabled
            ? RegistryStore.SetString(path, NoOpenWith, "")
            : RegistryStore.DeleteValues(path, NoOpenWith);
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        RegistryStore.SetString(RegistryPath.Parse(entry.Location), FriendlyAppName, newName);

    public Result Delete(ContextMenuEntry entry) =>
        RegistryStore.DeleteTree(RegistryPath.Parse(entry.Location));

    /// <summary>Registers a program for "Open with": <c>Applications\&lt;exe&gt;\shell\open\command</c> and an optional name.</summary>
    public Result CreateApp(string exePath, string? friendlyName, bool allUsers)
    {
        var full = exePath.Trim().Trim('"');
        var file = Path.GetFileName(full);
        if (file.Length == 0 || !file.Contains('.') || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Result.Failure(ErrorKind.InvalidInput, $"'{file}' is not a program file name.");
        }

        if (!File.Exists(full))
        {
            return Result.Failure(ErrorKind.NotFound, $"Not found: {full}");
        }

        var app = (allUsers ? roots.Writable[^1] : roots.Writable[0]).Path / "Applications" / file;
        if (RegistryStore.Exists(app))
        {
            return Result.Failure(ErrorKind.InvalidInput, $"{file} is already registered here.");
        }

        var result = RegistryStore.Write(app / "shell" / "open" / "command", key => key.SetValue("", $"\"{full}\" \"%1\""), create: true);
        if (result.IsSuccess && !string.IsNullOrWhiteSpace(friendlyName))
        {
            result = RegistryStore.SetString(app, FriendlyAppName, friendlyName.Trim());
        }

        if (!result.IsSuccess)
        {
            RegistryStore.DeleteTree(app);
        }

        return result;
    }
}
