using System.Globalization;

namespace NeoContext.Core.Dictionaries;

/// <summary>How a value is stored in the registry.</summary>
public enum RegValueKind
{
    Text,
    ExpandString,
    DWord,
    Binary,
}

public sealed record RegValue(string Name, RegValueKind Kind, string Data);

/// <summary>A small script the dictionary asks to be written to disk and then run by a command.</summary>
public sealed record ScriptFile(string FileName, string Content);

/// <summary>How the original ShellExecute element asks a program to be started.</summary>
/// <param name="Verb">"runas" starts it elevated.</param>
/// <param name="WindowStyle">0 = hidden, 1 = normal, 3 = maximized.</param>
public sealed record ShellExecuteSpec(string? Verb, int? WindowStyle);

/// <summary>
/// A <c>command</c> subkey. Either <paramref name="Default"/> is the whole command line, or the line is
/// built from a program, optional script files and arguments.
/// </summary>
public sealed record CommandSpec(
    string? Default,
    string Program,
    ScriptFile? ProgramScript,
    string Arguments,
    ScriptFile? ArgumentsScript,
    string Prefix,
    string Suffix,
    ShellExecuteSpec? Execute);

/// <summary>A registry key to create, with its values and subkeys. A node with a <see cref="Command"/> is a <c>command</c> subkey.</summary>
public sealed record RegNode(
    string Name,
    string? DefaultValue,
    IReadOnlyList<RegValue> Values,
    CommandSpec? Command,
    IReadOnlyList<RegNode> Children);

public enum EnhanceKind
{
    /// <summary>A verb under <c>shell</c>.</summary>
    Verb,

    /// <summary>A COM handler under <c>shellex\ContextMenuHandlers</c>.</summary>
    Handler,
}

/// <summary>One ready-made menu item the user can switch on.</summary>
/// <param name="Text">The name. For verbs it is the <c>MUIVerb</c> value and may be an indirect string like <c>@shell32.dll,-31236</c>.</param>
/// <param name="Tip">Extra explanation, or null.</param>
/// <param name="Node">The registry tree of a verb (the item key itself); null for handlers.</param>
/// <param name="Clsid">The CLSID of a handler; null for verbs.</param>
public sealed record EnhanceItem(EnhanceKind Kind, string KeyName, string Text, string? Tip, string? Icon, RegNode? Node, Guid? Clsid);

/// <summary>A scene (such as "Files" or "Drive") and the ready-made items for it.</summary>
/// <param name="RegPath">The class key, such as <c>HKEY_CLASSES_ROOT\Directory</c>.</param>
public sealed record EnhanceGroup(string Text, string? Icon, string RegPath, IReadOnlyList<EnhanceItem> Items);

/// <summary>What the dictionary conditions (<c>OSVersion</c>, <c>FileExists</c>, <c>Culture</c>) are checked against.</summary>
public sealed record DictionaryContext(string Culture, Version OsVersion, Func<string, bool> FileExists)
{
    public static DictionaryContext ForCurrentSystem(CultureInfo culture) => new(
        culture.Name,
        Environment.OSVersion.Version,
        path => File.Exists(Environment.ExpandEnvironmentVariables(path)));
}
