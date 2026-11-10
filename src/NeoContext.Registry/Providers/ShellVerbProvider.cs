using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;

namespace NeoContext.Registry.Providers;

/// <summary>
/// Static verbs: <c>&lt;class&gt;\shell\&lt;verb&gt;</c> keys with a <c>command</c> subkey.
/// <para>
/// A verb is hidden with the documented <c>LegacyDisable</c> value. When enabling, NeoContext
/// removes <c>LegacyDisable</c> and <c>ProgrammaticAccessOnly</c> but leaves <c>CommandFlags</c>
/// alone: that value holds other flags too, and the original app wiped it.
/// </para>
/// </summary>
public sealed class ShellVerbProvider(ClassesRoots roots, IIndirectStringResolver resolver) : IEntryProvider
{
    internal const string LegacyDisable = "LegacyDisable";
    internal const string ProgrammaticAccessOnly = "ProgrammaticAccessOnly";
    internal const string MuiVerb = "MUIVerb";

    private const EntryCapabilities Capabilities = EntryCapabilities.Toggle | EntryCapabilities.Rename | EntryCapabilities.Delete;

    public EntryKind Kind => EntryKind.ShellVerb;

    public bool ChangesRequireExplorerRestart => false;

    public Result<IReadOnlyList<ContextMenuEntry>> Load()
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var (section, classKeys) in ShellScopes.Keys)
        {
            foreach (var classKey in classKeys)
            {
                entries.AddRange(LoadClass(section, classKey));
            }
        }

        return entries;
    }

    /// <summary>Verbs under <c>&lt;class key&gt;\shell</c> in every writable root. Also used for per-extension classes.</summary>
    public List<ContextMenuEntry> LoadClass(MenuSection section, string classKey)
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var root in roots.Writable)
        {
            var shellPath = root.Path / classKey / "shell";
            using var shellKey = RegistryStore.OpenRead(shellPath);
            if (shellKey is null)
            {
                continue;
            }

            foreach (var verb in shellKey.GetSubKeyNames())
            {
                using var verbKey = shellKey.OpenSubKey(verb);
                if (verbKey is not null)
                {
                    entries.Add(ToEntry(section, root, shellPath / verb, verbKey));
                }
            }
        }

        return entries;
    }

    private ContextMenuEntry ToEntry(MenuSection section, ClassesRoot root, RegistryPath path, Microsoft.Win32.RegistryKey key)
    {
        var verb = path.Name;
        var names = key.GetValueNames();
        bool Has(string name) => names.Contains(name, StringComparer.OrdinalIgnoreCase);

        // Name precedence, as Explorer uses it: MUIVerb, then the default value, then the key name.
        var displayName = DisplayText.FirstReadable(resolver, key.GetValue(MuiVerb) as string, key.GetValue("") as string) ?? verb;
        var disabled = Has(LegacyDisable) || Has(ProgrammaticAccessOnly);

        using var commandKey = key.OpenSubKey("command");
        var command = commandKey?.GetValue("") as string;

        return new ContextMenuEntry(
            Id: path.ToString(),
            Kind: EntryKind.ShellVerb,
            Section: section,
            Key: verb,
            DisplayName: displayName,
            State: disabled ? EntryState.Disabled : EntryState.Enabled,
            Location: path.ToString(),
            Capabilities: Capabilities)
        {
            Command = command,
            Description = FileDescriptions.Describe(command),
            Publisher = FileDescriptions.Publisher(command),
            IconLocation = key.GetValue("Icon") as string,
            ExtendedOnly = Has("Extended"),
            Detail = root.Label,
        };
    }

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        var path = RegistryPath.Parse(entry.Location);
        return state == EntryState.Disabled
            ? RegistryStore.SetString(path, LegacyDisable, "")
            : RegistryStore.DeleteValues(path, LegacyDisable, ProgrammaticAccessOnly);
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        RegistryStore.SetString(RegistryPath.Parse(entry.Location), MuiVerb, newName);

    public Result Delete(ContextMenuEntry entry) =>
        RegistryStore.DeleteTree(RegistryPath.Parse(entry.Location));
}
