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
public sealed class ShellVerbProvider(
    ClassesRoots roots,
    IIndirectStringResolver resolver,
    RegistryPath? commandStore = null,
    Func<string?>? customPath = null) : IEntryProvider, IEntryEditor
{
    internal const string LegacyDisable = "LegacyDisable";
    internal const string ProgrammaticAccessOnly = "ProgrammaticAccessOnly";
    internal const string MuiVerb = "MUIVerb";

    private const EntryCapabilities Capabilities =
        EntryCapabilities.Toggle | EntryCapabilities.Rename | EntryCapabilities.Delete | EntryCapabilities.Edit;

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

        // The shared command pool, and the key the user picked on the "Custom registry path" page.
        if (commandStore is { } store)
        {
            entries.AddRange(LoadVerbs(MenuSection.CommandStore, store, ClassesRoots.AllUsersLabel));
        }

        if (RegistryPath.TryParse(customPath?.Invoke(), out var custom))
        {
            entries.AddRange(LoadVerbs(MenuSection.CustomPath, custom / "shell", ScopeLabel(custom)));
        }

        return entries;
    }

    /// <summary>The scope label for a key the user chose: per-user for HKCU, otherwise machine-wide.</summary>
    internal static string ScopeLabel(RegistryPath path) =>
        path.Hive == Microsoft.Win32.RegistryHive.CurrentUser ? ClassesRoots.CurrentUserLabel : ClassesRoots.AllUsersLabel;

    /// <summary>Verbs under <c>&lt;class key&gt;\shell</c> in every writable root. Also used for per-extension classes.</summary>
    public List<ContextMenuEntry> LoadClass(MenuSection section, string classKey)
    {
        var entries = new List<ContextMenuEntry>();
        foreach (var root in roots.Writable)
        {
            entries.AddRange(LoadVerbs(section, root.Path / classKey / "shell", root.Label));
        }

        return entries;
    }

    /// <summary>The verbs directly under a <c>shell</c> key.</summary>
    private List<ContextMenuEntry> LoadVerbs(MenuSection section, RegistryPath shellPath, string label)
    {
        var entries = new List<ContextMenuEntry>();
        using var shellKey = RegistryStore.OpenRead(shellPath);
        if (shellKey is null)
        {
            return entries;
        }

        foreach (var verb in shellKey.GetSubKeyNames())
        {
            using var verbKey = shellKey.OpenSubKey(verb);
            if (verbKey is not null)
            {
                entries.Add(ToEntry(section, label, shellPath / verb, verbKey));
            }
        }

        return entries;
    }

    private ContextMenuEntry ToEntry(MenuSection section, string label, RegistryPath path, Microsoft.Win32.RegistryKey key)
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
            // The shared command pool belongs to Windows: its verbs can be switched off or edited, but not deleted.
            Capabilities: section == MenuSection.CommandStore ? Capabilities & ~EntryCapabilities.Delete : Capabilities)
        {
            Command = command,
            Description = FileDescriptions.Describe(command),
            Publisher = FileDescriptions.Publisher(command),
            IconLocation = key.GetValue("Icon") as string,
            ExtendedOnly = Has("Extended"),
            Attributes = new VerbAttributes
            {
                Position = (key.GetValue("Position") as string)?.Trim().ToLowerInvariant() switch
                {
                    "top" => VerbPosition.Top,
                    "bottom" => VerbPosition.Bottom,
                    _ => VerbPosition.Default,
                },
                Extended = Has("Extended"),
                OnlyInBrowserWindow = Has("OnlyInBrowserWindow"),
                NoWorkingDirectory = Has("NoWorkingDirectory"),
                NeverDefault = Has("NeverDefault"),
                ShowAsDisabledIfHidden = Has("ShowAsDisabledIfHidden"),
                Shield = Has("HasLUAShield"),
            },
            Detail = label,
        };
    }

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        var path = RegistryPath.Parse(entry.Location);
        return state == EntryState.Disabled
            ? RegistryStore.SetString(path, LegacyDisable, "")
            : RegistryStore.DeleteValues(path, LegacyDisable, ProgrammaticAccessOnly);
    }

    /// <summary>Writes the icon, position and flags on the verb key, then the command on its <c>command</c> subkey.</summary>
    public Result Edit(ContextMenuEntry entry, EntryEdit edit)
    {
        var path = RegistryPath.Parse(entry.Location);
        var result = RegistryStore.Write(path, key => ApplyIconAndAttributes(key, edit.IconLocation, edit.Attributes));

        return !result.IsSuccess || edit.Command is null
            ? result
            : RegistryStore.Write(path / "command", key => key.SetValue("", edit.Command), create: true);
    }

    /// <summary>
    /// Writes the icon (null = unchanged, empty = remove), the position and the flags on a verb key.
    /// Shared by editing an existing verb and creating a new one.
    /// </summary>
    internal static void ApplyIconAndAttributes(Microsoft.Win32.RegistryKey key, string? icon, VerbAttributes attributes)
    {
        if (icon is not null)
        {
            if (icon.Length == 0)
            {
                key.DeleteValue("Icon", throwOnMissingValue: false);
            }
            else
            {
                key.SetValue("Icon", icon);
            }
        }

        SetFlag(key, "HasLUAShield", attributes.Shield);
        SetFlag(key, "Extended", attributes.Extended);
        SetFlag(key, "OnlyInBrowserWindow", attributes.OnlyInBrowserWindow);
        SetFlag(key, "NoWorkingDirectory", attributes.NoWorkingDirectory);
        SetFlag(key, "NeverDefault", attributes.NeverDefault);
        SetFlag(key, "ShowAsDisabledIfHidden", attributes.ShowAsDisabledIfHidden);

        if (attributes.Position == VerbPosition.Default)
        {
            key.DeleteValue("Position", throwOnMissingValue: false);
        }
        else
        {
            key.SetValue("Position", attributes.Position == VerbPosition.Top ? "Top" : "Bottom");
        }
    }

    /// <summary>A flag is an empty value; an existing value is kept as it is, because some apps store data in it.</summary>
    private static void SetFlag(Microsoft.Win32.RegistryKey key, string name, bool on)
    {
        if (!on)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else if (key.GetValue(name) is null)
        {
            key.SetValue(name, "");
        }
    }

    public Result Rename(ContextMenuEntry entry, string newName) =>
        RegistryStore.SetString(RegistryPath.Parse(entry.Location), MuiVerb, newName);

    public Result Delete(ContextMenuEntry entry) =>
        RegistryStore.DeleteTree(RegistryPath.Parse(entry.Location));
}
