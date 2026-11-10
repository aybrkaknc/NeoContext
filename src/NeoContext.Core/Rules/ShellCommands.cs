using NeoContext.Core.Models;

namespace NeoContext.Core.Rules;

/// <summary>Builds the command lines and script text for new verbs, and picks registry key names.</summary>
public static class ShellCommands
{
    /// <summary>
    /// What Explorer substitutes for the clicked item on this page: <c>"%1"</c> for files,
    /// <c>"%V"</c> for folders, drives, libraries and folder backgrounds, and nothing where nothing is clicked.
    /// </summary>
    public static string SelectionToken(MenuSection section) => section switch
    {
        MenuSection.Files or MenuSection.FilesAndFolders => "\"%1\"",
        MenuSection.Directory or MenuSection.Folder or MenuSection.FolderBackground or MenuSection.Drives or MenuSection.Library => "\"%V\"",
        _ => "",
    };

    /// <summary>Wraps a path with spaces in quotes. A path that is already quoted is kept.</summary>
    public static string Quote(string program)
    {
        var text = program.Trim();
        return text.StartsWith('"') || !text.Contains(' ') ? text : $"\"{text}\"";
    }

    /// <summary>A plain command line: the program, its arguments, then the selection token.</summary>
    public static string Direct(string program, string? arguments, string selectionToken) =>
        string.Join(' ', new[] { Quote(program), arguments?.Trim(), selectionToken }.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>The registry command that runs the script. The clicked item is its only argument.</summary>
    public static string ScriptCommandLine(string scriptPath, NewVerbScript script) =>
        $"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"" +
        (script.SelectionToken.Length > 0 ? $" {script.SelectionToken}" : "");

    /// <summary>
    /// The script text. The fixed arguments are escaped into a single-quoted string; the clicked item
    /// arrives as <c>$Target</c> and is only ever used as data.
    /// </summary>
    public static string ScriptContent(NewVerbScript script)
    {
        var lines = new List<string> { "param([string]$Target)" };

        // A template marks where the clicked item goes (%1, %V); otherwise the item is appended in quotes.
        if (HasPlaceholder(script.Arguments))
        {
            lines.Add($"$arguments = {PowerShellQuote(script.Arguments.Trim())}.Replace('%1', $Target).Replace('%V', $Target).Replace('%v', $Target)");
        }
        else
        {
            lines.Add($"$arguments = {PowerShellQuote(script.Arguments.Trim())}");
            lines.Add("if ($Target) { $arguments = ($arguments + ' \"' + $Target + '\"').Trim() }");
        }

        lines.Add($"$start = @{{ FilePath = {PowerShellQuote(script.Program.Trim().Trim('"'))} }}");
        lines.Add("if ($arguments) { $start.ArgumentList = $arguments }");

        if (script.RunAsAdministrator)
        {
            lines.Add("$start.Verb = 'RunAs'");
        }

        if (script.Hidden)
        {
            lines.Add("$start.WindowStyle = 'Hidden'");
        }
        else if (script.Maximized)
        {
            lines.Add("$start.WindowStyle = 'Maximized'");
        }

        lines.Add("Start-Process @start");
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>True when the arguments say where the clicked item goes, with %1 or %V (Explorer reads %v too).</summary>
    public static bool HasPlaceholder(string arguments) =>
        arguments.Contains("%1", StringComparison.Ordinal) || arguments.Contains("%V", StringComparison.OrdinalIgnoreCase);

    /// <summary>PowerShell reads a single-quoted string literally, except that a quote is doubled. Typographic quotes count as quotes too.</summary>
    private static string PowerShellQuote(string text)
    {
        var escaped = string.Concat(text.Select(c => c is '\'' or '‘' or '’' or '‚' or '‛' ? $"{c}{c}" : c.ToString()));
        return $"'{escaped}'";
    }

    /// <summary>A registry key name from a display name: letters and digits only, made unique with a number.</summary>
    public static string KeyName(string displayName, Func<string, bool> exists)
    {
        var slug = new string([.. displayName.Where(char.IsLetterOrDigit).Take(40)]);
        if (slug.Length == 0)
        {
            slug = "Item";
        }

        var candidate = slug;
        for (var number = 2; exists(candidate); number++)
        {
            candidate = $"{slug}{number}";
        }

        return candidate;
    }
}
