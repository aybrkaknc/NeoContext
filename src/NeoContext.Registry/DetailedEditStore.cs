using System.Globalization;
using Microsoft.Win32;
using NeoContext.Core.Dictionaries;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;

namespace NeoContext.Registry;

/// <summary>
/// Reads and writes the registry values behind the "Detailed editing" settings.
/// <para>
/// A setting is on when the first rule that matches decides so; a rule whose value is missing (or of another
/// type) is skipped unless it is the last one; when nothing matches the setting counts as on. Switching writes
/// every rule: the On or Off value, or deletes the value when that value is null. Rules for machine-wide
/// policies need administrator rights.
/// </para>
/// </summary>
public sealed class DetailedEditStore(ClassesRoots roots)
{
    public static bool IsOn(DetailedItem item)
    {
        for (var i = 0; i < item.Rules.Count; i++)
        {
            var rule = item.Rules[i];
            var (value, kind, _) = Read(rule);
            if ((value is null || kind != rule.Kind) && i < item.Rules.Count - 1)
            {
                continue;
            }

            if (value == rule.On?.ToLowerInvariant())
            {
                return true;
            }

            if (value == rule.Off?.ToLowerInvariant())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Applies every rule, even when one fails, and reports the first failure.</summary>
    public static Result Set(DetailedItem item, bool on)
    {
        var first = Result.Success();
        foreach (var rule in item.Rules)
        {
            var result = Apply(rule, on ? rule.On : rule.Off);
            if (!result.IsSuccess && first.IsSuccess)
            {
                first = result;
            }
        }

        return first;
    }

    /// <summary>The stored number or text of a value setting (its first rule), or empty when none is stored.</summary>
    public static string ReadValue(DetailedItem item) => Read(item.Rules[0]).Raw ?? "";

    /// <summary>
    /// Writes a number or text setting. An empty text removes the value, so Windows uses its default.
    /// A number must be a whole number inside the item's limits.
    /// </summary>
    public static Result SetValue(DetailedItem item, string text)
    {
        var rule = item.Rules[0];
        text = text.Trim();
        if (text.Length == 0)
        {
            return Apply(rule, null);
        }

        if (item.Kind == DetailedKind.Number
            && (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                || number < (rule.Min ?? int.MinValue) || number > (rule.Max ?? int.MaxValue)))
        {
            return Result.Failure(ErrorKind.InvalidInput, $"'{text}' is not a whole number from {rule.Min} to {rule.Max}.");
        }

        return Apply(rule, text);
    }

    /// <summary>The file of the program that owns this CLSID, or null when it is not installed.</summary>
    public string? ClsidFilePath(Guid clsid)
    {
        var id = clsid.ToString("B");
        RegistryPath[] classes =
        [
            roots.Merged / "CLSID" / id,
            roots.Merged / "WOW6432Node" / "CLSID" / id,
            new(RegistryHive.LocalMachine, $@"SOFTWARE\WOW6432Node\Classes\CLSID\{id}"),
        ];
        foreach (var path in classes)
        {
            foreach (var server in new[] { "InprocServer32", "LocalServer32" })
            {
                if (FileDescriptions.ExtractFile(RegistryStore.GetString(path / server, "")) is { } file)
                {
                    return file;
                }
            }
        }

        return null;
    }

    public bool IsInstalled(Guid clsid) => ClsidFilePath(clsid) is not null;

    private static Result Apply(DetailedRule rule, string? data)
    {
        if (!RegistryPath.TryParse(rule.RegPath, out var path))
        {
            return Result.Failure(ErrorKind.InvalidInput, $"'{rule.RegPath}' is not a registry path.");
        }

        if (data is null)
        {
            // A value that is already gone (or whose key does not exist) is what the rule asks for.
            var removed = RegistryStore.DeleteValues(path, rule.ValueName);
            return removed.Error?.Kind == ErrorKind.NotFound ? Result.Success() : removed;
        }

        return RegistryStore.Write(path, key => Write(key, rule, data), create: true);
    }

    private static void Write(RegistryKey key, DetailedRule rule, string data)
    {
        switch (rule.Kind)
        {
            case RegValueKind.DWord:
                key.SetValue(rule.ValueName, int.Parse(data, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                break;
            case RegValueKind.ExpandString:
                key.SetValue(rule.ValueName, data, RegistryValueKind.ExpandString);
                break;
            case RegValueKind.Binary:
                key.SetValue(rule.ValueName, Convert.FromHexString(data.Replace(" ", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal)), RegistryValueKind.Binary);
                break;
            default:
                key.SetValue(rule.ValueName, data, RegistryValueKind.String);
                break;
        }
    }

    /// <summary>The value as lower-case text (numbers in decimal, bytes as "00 01"), its type and the text as stored; null when it does not exist.</summary>
    private static (string? Value, RegValueKind? Kind, string? Raw) Read(DetailedRule rule)
    {
        if (!RegistryPath.TryParse(rule.RegPath, out var path))
        {
            return (null, null, null);
        }

        using var key = RegistryStore.OpenRead(path);
        var raw = key?.GetValue(rule.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            return (null, null, null);
        }

        var kind = key!.GetValueKind(rule.ValueName) switch
        {
            RegistryValueKind.DWord => RegValueKind.DWord,
            RegistryValueKind.ExpandString => RegValueKind.ExpandString,
            RegistryValueKind.Binary => RegValueKind.Binary,
            _ => RegValueKind.Text,
        };
        var text = raw switch
        {
            byte[] bytes => string.Join(' ', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture))),
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "",
        };
        return (text.ToLowerInvariant(), kind, text);
    }
}
