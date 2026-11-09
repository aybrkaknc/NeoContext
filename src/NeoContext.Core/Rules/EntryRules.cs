using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Core.Rules;

/// <summary>Validation for names typed by the user.</summary>
public static class EntryNameRules
{
    /// <summary>Explorer truncates or ignores verb names (MUIVerb) longer than this.</summary>
    public const int MaxLength = 80;

    public static Result Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(ErrorKind.InvalidInput, "The name cannot be empty.");
        }

        if (name.Trim().Length > MaxLength)
        {
            return Result.Failure(ErrorKind.InvalidInput, $"The name cannot be longer than {MaxLength} characters.");
        }

        return Result.Success();
    }
}

/// <summary>
/// Safety rules for entries whose removal can break basic Explorer behaviour.
/// The user can turn protection off in the settings.
/// </summary>
public sealed class ProtectionPolicy(bool enabled)
{
    /// <summary>The "Open" handler for .lnk shortcuts. Disabling it stops shortcuts from opening.</summary>
    public static readonly Guid ShortcutOpenHandler = new("00021401-0000-0000-c000-000000000046");

    /// <summary>Can change at runtime when the user toggles the setting.</summary>
    public bool Enabled { get; set; } = enabled;

    /// <summary>True when disabling or deleting <paramref name="entry"/> must be refused.</summary>
    public bool IsProtected(ContextMenuEntry entry)
    {
        if (!Enabled)
        {
            return false;
        }

        return entry.Kind switch
        {
            EntryKind.ShellVerb => string.Equals(entry.Key, "open", StringComparison.OrdinalIgnoreCase),
            EntryKind.ShellExtension => Guid.TryParse(entry.Key, out var clsid) && clsid == ShortcutOpenHandler,
            _ => false,
        };
    }
}
