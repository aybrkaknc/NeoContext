using NeoContext.Core.Abstractions;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;

namespace NeoContext.Core.Services;

/// <summary>
/// The single entry point the UI talks to. It loads entries from every provider, applies
/// validation and protection rules, and routes each change to the provider that owns the entry.
/// </summary>
public sealed class ContextMenuService
{
    private readonly Dictionary<EntryKind, IEntryProvider> _providers;
    private readonly ProtectionPolicy _protection;
    private IReadOnlyList<ContextMenuEntry> _entries = [];

    public ContextMenuService(IEnumerable<IEntryProvider> providers, ProtectionPolicy protection)
    {
        _providers = providers.ToDictionary(p => p.Kind);
        _protection = protection;
    }

    /// <summary>The entries from the last <see cref="Refresh"/>.</summary>
    public IReadOnlyList<ContextMenuEntry> Entries => _entries;

    /// <summary>Errors from providers that failed during the last <see cref="Refresh"/>.</summary>
    public IReadOnlyList<Error> LoadErrors { get; private set; } = [];

    /// <summary>
    /// Reloads every provider. One failing provider does not hide the others: its error is
    /// collected in <see cref="LoadErrors"/> and the rest of the entries are still returned.
    /// </summary>
    public IReadOnlyList<ContextMenuEntry> Refresh()
    {
        var entries = new List<ContextMenuEntry>();
        var errors = new List<Error>();

        foreach (var provider in _providers.Values)
        {
            var result = provider.Load();
            if (result.IsSuccess)
            {
                entries.AddRange(result.Value);
            }
            else
            {
                errors.Add(result.Error!);
            }
        }

        _entries = entries;
        LoadErrors = errors;
        return _entries;
    }

    public IEnumerable<ContextMenuEntry> GetSection(MenuSection section) =>
        _entries.Where(e => e.Section == section).OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase);

    public bool IsProtected(ContextMenuEntry entry) => _protection.IsProtected(entry);

    /// <summary>True when a change to this entry only shows up after Explorer restarts.</summary>
    public bool RequiresExplorerRestart(ContextMenuEntry entry) =>
        entry.RestartExplorer || (_providers.TryGetValue(entry.Kind, out var provider) && provider.ChangesRequireExplorerRestart);

    public Result SetState(ContextMenuEntry entry, EntryState state)
    {
        if (entry.State == state)
        {
            return Result.Success();
        }

        if (!entry.Can(EntryCapabilities.Toggle))
        {
            return NotSupported(entry, "toggled");
        }

        if (state == EntryState.Disabled && _protection.IsProtected(entry))
        {
            return Result.Failure(ErrorKind.Protected, $"'{entry.DisplayName}' is protected and cannot be disabled.");
        }

        return WithProvider(entry, p => p.SetState(entry, state));
    }

    public Result Rename(ContextMenuEntry entry, string newName)
    {
        if (!entry.Can(EntryCapabilities.Rename))
        {
            return NotSupported(entry, "renamed");
        }

        var validation = EntryNameRules.Validate(newName);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        return WithProvider(entry, p => p.Rename(entry, newName.Trim()));
    }

    public Result Edit(ContextMenuEntry entry, EntryEdit edit)
    {
        if (!entry.Can(EntryCapabilities.Edit))
        {
            return NotSupported(entry, "edited");
        }

        if (_protection.IsProtected(entry))
        {
            return Result.Failure(ErrorKind.Protected, $"'{entry.DisplayName}' is protected and cannot be edited.");
        }

        if (edit.Command is { } command && string.IsNullOrWhiteSpace(command))
        {
            return Result.Failure(ErrorKind.InvalidInput, "The command cannot be empty.");
        }

        return _providers.TryGetValue(entry.Kind, out var provider) && provider is IEntryEditor editor
            ? editor.Edit(entry, edit)
            : Result.Failure(ErrorKind.NotSupported, $"No editor is registered for {entry.Kind}.");
    }

    public Result Move(ContextMenuEntry entry, int direction)
    {
        if (!entry.Can(EntryCapabilities.Move))
        {
            return NotSupported(entry, "moved");
        }

        return _providers.TryGetValue(entry.Kind, out var provider) && provider is IEntryMover mover
            ? mover.Move(entry, direction)
            : Result.Failure(ErrorKind.NotSupported, $"No mover is registered for {entry.Kind}.");
    }

    public Result Delete(ContextMenuEntry entry)
    {
        if (!entry.Can(EntryCapabilities.Delete))
        {
            return NotSupported(entry, "deleted");
        }

        if (_protection.IsProtected(entry))
        {
            return Result.Failure(ErrorKind.Protected, $"'{entry.DisplayName}' is protected and cannot be deleted.");
        }

        return WithProvider(entry, p => p.Delete(entry));
    }

    private Result WithProvider(ContextMenuEntry entry, Func<IEntryProvider, Result> action) =>
        _providers.TryGetValue(entry.Kind, out var provider)
            ? action(provider)
            : Result.Failure(ErrorKind.NotSupported, $"No provider is registered for {entry.Kind}.");

    private static Result NotSupported(ContextMenuEntry entry, string verb) =>
        Result.Failure(ErrorKind.NotSupported, $"'{entry.DisplayName}' cannot be {verb}.");
}
