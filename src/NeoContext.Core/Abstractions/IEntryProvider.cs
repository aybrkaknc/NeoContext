using NeoContext.Core.Models;
using NeoContext.Core.Results;

namespace NeoContext.Core.Abstractions;

/// <summary>
/// Reads and changes one kind of context menu entry. Providers do not validate names or apply
/// protection rules; <see cref="Services.ContextMenuService"/> does that before calling them.
/// Providers never show UI and never throw for expected failures; they return a <see cref="Result"/>.
/// </summary>
public interface IEntryProvider
{
    EntryKind Kind { get; }

    /// <summary>True when Explorer only picks up changes after a restart (e.g. the Win+X menu).</summary>
    bool ChangesRequireExplorerRestart { get; }

    /// <summary>Reads all entries of this kind.</summary>
    Result<IReadOnlyList<ContextMenuEntry>> Load();

    Result SetState(ContextMenuEntry entry, EntryState state);

    Result Rename(ContextMenuEntry entry, string newName);

    Result Delete(ContextMenuEntry entry);
}

/// <summary>Resolves indirect strings such as <c>@shell32.dll,-8496</c> to display text.</summary>
public interface IIndirectStringResolver
{
    /// <summary>Returns the resolved text, or null when the resource cannot be loaded.</summary>
    string? Resolve(string indirectString);
}

/// <summary>A resolver that leaves strings unresolved. Used in tests and as a safe default.</summary>
public sealed class NullIndirectStringResolver : IIndirectStringResolver
{
    public static readonly NullIndirectStringResolver Instance = new();

    public string? Resolve(string indirectString) => null;
}
