using System.Text;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;
using NeoContext.Registry.Providers;

namespace NeoContext.Registry;

/// <summary>
/// Creates new menu entries: a verb (single command or submenu) or a shell extension handler.
/// Entries go to the per-user classes by default and to the machine-wide classes when asked.
/// If any step fails, what was already written is removed again, so no half-made entry stays behind.
/// </summary>
public sealed class EntryCreator(ClassesRoots roots, string scriptsDirectory)
{
    /// <summary>True when the CLSID is registered on this PC.</summary>
    public bool ClsidExists(Guid clsid) => RegistryStore.Exists(roots.Merged / "CLSID" / clsid.ToString("B"));

    public Result CreateVerb(MenuSection section, NewVerb verb, bool allUsers)
    {
        if (!TryGetClassKey(section, out var classKey))
        {
            return Result.Failure(ErrorKind.NotSupported, $"Nothing can be added to {section}.");
        }

        var shell = PickRoot(allUsers).Path / classKey / "shell";
        var keyName = ShellCommands.KeyName(verb.Name, name => RegistryStore.Exists(shell / name));
        var path = shell / keyName;

        var command = verb.Command;
        string? scriptFile = null;
        if (verb.Script is { } script)
        {
            scriptFile = Path.Combine(scriptsDirectory, $"{keyName}.ps1");
            command = ShellCommands.ScriptCommandLine(scriptFile, script);
            try
            {
                Directory.CreateDirectory(scriptsDirectory);
                File.WriteAllText(scriptFile, ShellCommands.ScriptContent(script), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Failure(ErrorKind.Unexpected, $"Could not write the script {scriptFile}: {ex.Message}");
            }
        }

        var result = RegistryStore.Write(path, key =>
        {
            key.SetValue(ShellVerbProvider.MuiVerb, verb.Name.Trim());
            ShellVerbProvider.ApplyIconAndAttributes(key, verb.IconLocation, verb.Attributes);
            if (verb.IsSubmenu)
            {
                // An empty SubCommands value turns the verb into a cascading menu built from its own "shell" subkey.
                key.SetValue("SubCommands", "");
            }
        }, create: true);

        if (result.IsSuccess && command is not null)
        {
            result = RegistryStore.Write(path / "command", key => key.SetValue("", command), create: true);
        }

        for (var i = 0; result.IsSuccess && i < verb.Children.Count; i++)
        {
            var child = verb.Children[i];
            var childShell = path / "shell";
            var childName = ShellCommands.KeyName(child.Name, name => RegistryStore.Exists(childShell / name));
            result = RegistryStore.Write(childShell / childName / "command", key => key.SetValue("", child.Command), create: true);
            if (result.IsSuccess)
            {
                result = RegistryStore.Write(childShell / childName, key => key.SetValue(ShellVerbProvider.MuiVerb, child.Name.Trim()));
            }
        }

        if (!result.IsSuccess)
        {
            RegistryStore.DeleteTree(path);
            if (scriptFile is not null && File.Exists(scriptFile))
            {
                File.Delete(scriptFile);
            }
        }

        return result;
    }

    public Result CreateHandler(MenuSection section, Guid clsid, bool allUsers)
    {
        if (!TryGetClassKey(section, out var classKey))
        {
            return Result.Failure(ErrorKind.NotSupported, $"Nothing can be added to {section}.");
        }

        var path = PickRoot(allUsers).Path / classKey / "shellex" / ShellExtensionProvider.ActiveKey / clsid.ToString("B");
        return RegistryStore.Exists(path)
            ? Result.Failure(ErrorKind.InvalidInput, $"The handler {clsid:B} is already listed here.")
            : RegistryStore.Write(path, key => key.SetValue("", clsid.ToString("B")), create: true);
    }

    private ClassesRoot PickRoot(bool allUsers) => allUsers ? roots.Writable[^1] : roots.Writable[0];

    private static bool TryGetClassKey(MenuSection section, out string classKey)
    {
        classKey = ShellScopes.Keys.TryGetValue(section, out var keys) ? keys[0] : "";
        return classKey.Length > 0;
    }
}
