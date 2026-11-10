using System.Globalization;
using System.Text;
using Microsoft.Win32;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;
using NeoContext.Registry.Providers;

namespace NeoContext.Registry;

/// <summary>
/// Switches ready-made dictionary items on and off by writing or deleting their registry keys.
/// <para>
/// An item counts as on when its key exists in any classes root. Switching on writes to the per-user
/// classes; switching off deletes from every root where it exists (which needs administrator rights for
/// machine-wide copies). Commands that need a hidden or elevated start run through a small PowerShell
/// script in <c>scriptsDirectory</c>, and dictionary script files (VBScript, batch) are written there too.
/// </para>
/// </summary>
public sealed class EnhanceMenuStore(ClassesRoots roots, string scriptsDirectory)
{
    private const string ClassesPrefix = @"HKEY_CLASSES_ROOT\";

    /// <summary>Only groups under HKEY_CLASSES_ROOT can be written to the per-user and machine classes.</summary>
    public static bool IsSupported(EnhanceGroup group) => group.RegPath.StartsWith(ClassesPrefix, StringComparison.OrdinalIgnoreCase);

    private static string ClassKey(EnhanceGroup group) => group.RegPath[ClassesPrefix.Length..].Trim('\\');

    /// <summary>Where the item lives, for display and for opening in Registry Editor.</summary>
    public static string Location(EnhanceGroup group, EnhanceItem item) => item.Kind == EnhanceKind.Verb
        ? $@"{group.RegPath}\shell\{item.KeyName}"
        : $@"{group.RegPath}\shellex\ContextMenuHandlers\{item.KeyName}";

    public bool IsEnabled(EnhanceGroup group, EnhanceItem item)
    {
        var classKey = ClassKey(group);
        return roots.Writable.Any(root => item.Kind == EnhanceKind.Verb
            ? RegistryStore.Exists(root.Path / classKey / "shell" / item.KeyName)
            : HandlerPaths(root, classKey, item.Clsid!.Value).Count > 0);
    }

    public Result Enable(EnhanceGroup group, EnhanceItem item)
    {
        if (!IsSupported(group))
        {
            return Result.Failure(ErrorKind.NotSupported, $"{group.RegPath} cannot be written.");
        }

        var classKey = ClassKey(group);
        var root = roots.Writable[0];
        if (item.Kind == EnhanceKind.Handler)
        {
            var handler = root.Path / classKey / "shellex" / "ContextMenuHandlers" / item.KeyName;
            return RegistryStore.Write(handler, key => key.SetValue("", item.Clsid!.Value.ToString("B")), create: true);
        }

        var path = root.Path / classKey / "shell" / item.KeyName;
        var scripts = new List<string>();
        var result = WriteNode(path, item.Node!, $"{Sanitize(classKey)}-{Sanitize(item.KeyName)}", scripts);
        if (!result.IsSuccess)
        {
            // Nothing half-made stays behind.
            RegistryStore.DeleteTree(path);
            foreach (var script in scripts.Where(File.Exists))
            {
                File.Delete(script);
            }
        }

        return result;
    }

    public Result Disable(EnhanceGroup group, EnhanceItem item)
    {
        var classKey = ClassKey(group);
        foreach (var root in roots.Writable)
        {
            var targets = item.Kind == EnhanceKind.Verb
                ? new List<RegistryPath> { root.Path / classKey / "shell" / item.KeyName }
                : HandlerPaths(root, classKey, item.Clsid!.Value);
            foreach (var target in targets.Where(RegistryStore.Exists))
            {
                var result = RegistryStore.DeleteTree(target);
                if (!result.IsSuccess)
                {
                    return result;
                }
            }
        }

        return Result.Success();
    }

    /// <summary>The first command line the item would write, for showing in the details. Writes nothing.</summary>
    public string? MainCommand(EnhanceGroup group, EnhanceItem item)
    {
        var node = item.Node is null ? null : FirstCommandNode(item.Node, "");
        if (node is null)
        {
            return null;
        }

        var line = BuildCommand(node.Value.Command, $"{Sanitize(ClassKey(group))}-{Sanitize(item.KeyName)}{node.Value.Suffix}", write: false, []);
        return line.IsSuccess ? line.Value : null;
    }

    private static (CommandSpec Command, string Suffix)? FirstCommandNode(RegNode node, string path)
    {
        if (node.Command is { } command)
        {
            return (command, path);
        }

        foreach (var child in node.Children)
        {
            if (FirstCommandNode(child, $"{path}-{Sanitize(child.Name)}") is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static List<RegistryPath> HandlerPaths(ClassesRoot root, string classKey, Guid clsid)
    {
        var found = new List<RegistryPath>();
        var handlers = root.Path / classKey / "shellex" / "ContextMenuHandlers";
        using var key = RegistryStore.OpenRead(handlers);
        if (key is null)
        {
            return found;
        }

        foreach (var name in key.GetSubKeyNames())
        {
            using var handler = key.OpenSubKey(name);
            if ((Guid.TryParse(handler?.GetValue("") as string, out var fromValue) && fromValue == clsid)
                || (Guid.TryParse(name, out var fromName) && fromName == clsid))
            {
                found.Add(handlers / name);
            }
        }

        return found;
    }

    private Result WriteNode(RegistryPath path, RegNode node, string scriptBase, List<string> scripts)
    {
        if (node.Command is { } command)
        {
            var line = BuildCommand(command, scriptBase, write: true, scripts);
            return line.IsSuccess
                ? RegistryStore.Write(path, key => key.SetValue("", line.Value), create: true)
                : line.ToResult();
        }

        var result = RegistryStore.Write(path, key =>
        {
            if (node.DefaultValue is not null)
            {
                key.SetValue("", node.DefaultValue);
            }

            foreach (var value in node.Values)
            {
                SetValue(key, value);
            }
        }, create: true);

        foreach (var child in node.Children)
        {
            if (!result.IsSuccess)
            {
                break;
            }

            result = WriteNode(path / child.Name, child, $"{scriptBase}-{Sanitize(child.Name)}", scripts);
        }

        return result;
    }

    private static void SetValue(RegistryKey key, RegValue value)
    {
        switch (value.Kind)
        {
            case RegValueKind.DWord:
                key.SetValue(value.Name, int.Parse(value.Data, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                break;
            case RegValueKind.ExpandString:
                key.SetValue(value.Name, value.Data, RegistryValueKind.ExpandString);
                break;
            case RegValueKind.Binary:
                key.SetValue(value.Name, Convert.FromHexString(value.Data.Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)), RegistryValueKind.Binary);
                break;
            default:
                key.SetValue(value.Name, value.Data, RegistryValueKind.String);
                break;
        }
    }

    /// <summary>
    /// The command line for a <c>command</c> key. A dictionary script file is written to the scripts folder
    /// and named in the line; an elevated, hidden or maximized start goes through a PowerShell script that
    /// receives the clicked item as its only argument.
    /// </summary>
    private Result<string> BuildCommand(CommandSpec spec, string scriptBase, bool write, List<string> scripts)
    {
        if (spec.Default is not null)
        {
            return spec.Default;
        }

        try
        {
            var program = Environment.ExpandEnvironmentVariables(spec.Program);
            if (spec.ProgramScript is { } programScript)
            {
                program = $"\"{PlaceScript(programScript, write, scripts)}\"";
            }

            var arguments = Environment.ExpandEnvironmentVariables(spec.Arguments);
            if (spec.ArgumentsScript is { } argumentsScript)
            {
                arguments = $"\"{PlaceScript(argumentsScript, write, scripts)}\"";
            }

            arguments = spec.Prefix + arguments + spec.Suffix;

            var execute = spec.Execute;
            var hidden = execute?.WindowStyle == 0;
            var maximized = execute?.WindowStyle == 3;
            var elevated = string.Equals(execute?.Verb, "runas", StringComparison.OrdinalIgnoreCase);
            if (!hidden && !maximized && !elevated)
            {
                return arguments.Length == 0 ? program : $"{program} {arguments}";
            }

            var token = !ShellCommands.HasPlaceholder(arguments) ? "" : arguments.Contains("%1", StringComparison.Ordinal) ? "\"%1\"" : "\"%V\"";
            var script = new NewVerbScript(program, arguments, token, hidden, elevated, maximized);
            var scriptFile = Path.Combine(scriptsDirectory, $"{scriptBase}.ps1");
            if (write)
            {
                WriteFile(scriptFile, ShellCommands.ScriptContent(script), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                scripts.Add(scriptFile);
            }

            return ShellCommands.ScriptCommandLine(scriptFile, script);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(ErrorKind.Unexpected, $"Could not write a script file: {ex.Message}");
        }
    }

    /// <summary>The path of a dictionary script file; the file is written unless this is a dry run.</summary>
    private string PlaceScript(ScriptFile script, bool write, List<string> scripts)
    {
        var path = Path.Combine(scriptsDirectory, Path.GetFileName(script.FileName));
        if (write)
        {
            // Batch files are read in the ANSI code page by cmd.exe; everything else (VBScript) is UTF-16 with a BOM.
            var encoding = Path.GetExtension(path).ToLowerInvariant() is ".bat" or ".cmd" ? new UTF8Encoding(false) : Encoding.Unicode;
            WriteFile(path, script.Content, encoding);
            scripts.Add(path);
        }

        return path;
    }

    private void WriteFile(string path, string content, Encoding encoding)
    {
        Directory.CreateDirectory(scriptsDirectory);
        File.WriteAllText(path, content, encoding);
    }

    private static string Sanitize(string text) =>
        string.Concat(text.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
