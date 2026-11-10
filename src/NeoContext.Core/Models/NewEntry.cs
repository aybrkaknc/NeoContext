namespace NeoContext.Core.Models;

/// <summary>One child of a new submenu.</summary>
public sealed record NewVerbChild(string Name, string Command);

/// <summary>
/// A program to run through a small PowerShell script, so its window can be hidden or it can ask for
/// administrator rights. The clicked item is passed to the script as a separate argument, never pasted
/// into script text, so a file name cannot inject commands.
/// </summary>
/// <param name="Program">The program to start.</param>
/// <param name="Arguments">Fixed arguments placed before the clicked item.</param>
/// <param name="SelectionToken">The token Explorer fills in (<c>"%1"</c> or <c>"%V"</c>), or empty to pass nothing.</param>
/// <param name="Hidden">Start the program with a hidden window.</param>
/// <param name="RunAsAdministrator">Start the program elevated.</param>
/// <param name="Maximized">Start the program with a maximized window (ignored when hidden).</param>
public sealed record NewVerbScript(string Program, string Arguments, string SelectionToken, bool Hidden, bool RunAsAdministrator, bool Maximized = false);

/// <summary>
/// A verb to create. It is a single command (<see cref="Command"/> or <see cref="Script"/>) or a
/// submenu with <see cref="Children"/>, never both.
/// </summary>
public sealed record NewVerb(
    string Name,
    string? Command,
    NewVerbScript? Script,
    string? IconLocation,
    VerbAttributes Attributes,
    IReadOnlyList<NewVerbChild> Children)
{
    /// <summary>Names of verbs from the shared command pool; the submenu is built from them instead of from <see cref="Children"/>.</summary>
    public IReadOnlyList<string> SharedCommands { get; init; } = [];

    public bool IsSubmenu => Children.Count > 0 || SharedCommands.Count > 0;
}
