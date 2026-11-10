using System.Collections.Concurrent;
using System.Diagnostics;

namespace NeoContext.Core.Rules;

/// <summary>
/// Reads the product description and the company Windows stores inside an executable or DLL
/// (for example "Notepad" by "Microsoft Corporation"), given a command line or a file path.
/// Used to tell the user what an entry runs and who made it.
/// </summary>
public static class FileDescriptions
{
    /// <summary>Hosts whose details say nothing about what the entry really does.</summary>
    private static readonly HashSet<string> GenericHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "rundll32", "cmd", "powershell", "pwsh", "mshta", "wscript", "cscript", "regsvr32", "msiexec", "explorer",
    };

    private static readonly ConcurrentDictionary<string, (string? Description, string? Publisher)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The description for the program named in <paramref name="commandOrPath"/>, or null.</summary>
    public static string? Describe(string? commandOrPath) => Lookup(commandOrPath).Description;

    /// <summary>The company that made the program named in <paramref name="commandOrPath"/>, or null.</summary>
    public static string? Publisher(string? commandOrPath) => Lookup(commandOrPath).Publisher;

    private static (string? Description, string? Publisher) Lookup(string? commandOrPath)
    {
        var file = ExtractFile(commandOrPath);
        return file is null ? (null, null) : Cache.GetOrAdd(file, Read);
    }

    /// <summary>Takes the program path from a command line such as <c>"C:\a b\x.exe" "%1"</c>.</summary>
    public static string? ExtractFile(string? commandOrPath)
    {
        if (string.IsNullOrWhiteSpace(commandOrPath))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(commandOrPath.Trim());
        string candidate;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end < 0)
            {
                return null;
            }

            candidate = text[1..end];
        }
        else if (File.Exists(text))
        {
            candidate = text;
        }
        else
        {
            // Unquoted: the program ends at the first ".exe" / ".dll" (paths may contain spaces).
            var match = System.Text.RegularExpressions.Regex.Match(text, @"^.+?\.(exe|dll|com|bat|cmd)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            candidate = match.Success ? match.Value : text.Split(' ')[0];
        }

        return Path.IsPathFullyQualified(candidate) && File.Exists(candidate) ? candidate : null;
    }

    private static (string? Description, string? Publisher) Read(string file)
    {
        if (GenericHosts.Contains(Path.GetFileNameWithoutExtension(file)))
        {
            return (null, null);
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            var description = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
            return (Clean(description), Clean(info.CompanyName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, null);
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
