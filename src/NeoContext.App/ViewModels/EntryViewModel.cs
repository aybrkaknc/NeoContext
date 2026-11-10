using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeoContext.App.Services;
using NeoContext.Core.Models;
using NeoContext.Registry;

namespace NeoContext.App.ViewModels;

/// <summary>One row in the entry list. Changes are forwarded to <see cref="MainViewModel"/>.</summary>
public sealed partial class EntryViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _isEnabled;
    private bool _isApplying;

    public EntryViewModel(ContextMenuEntry entry, MainViewModel owner, bool showSection = false)
    {
        Entry = entry;
        _owner = owner;
        _isEnabled = entry.IsEnabled;
        ShowSection = showSection;
    }

    public ContextMenuEntry Entry { get; }

    /// <summary>Search results span every page, so they carry a tag naming the page the entry is on.</summary>
    public bool ShowSection { get; }

    public string SectionLabel => Strings.Get($"Section_{Entry.Section}");

    public string Name => Entry.DisplayName;

    /// <summary>The second line: command or target when known, otherwise the location.</summary>
    public string Secondary => Entry.Command ?? Entry.Location;

    public string Location => Entry.Location;

    /// <summary>Full location, plus the CLSID for COM-based entries.</summary>
    public string Tooltip => Entry.Kind is EntryKind.ShellExtension or EntryKind.PackagedApp
        ? $"{Entry.Location}{Environment.NewLine}{Entry.Key}"
        : Entry.Location;

    /// <summary>The short tag on the right: scope, extension, Win+X group or item types.</summary>
    public string Detail => Entry.Detail switch
    {
        null => "",
        ClassesRoots.CurrentUserLabel => Strings.Get("Scope_CurrentUser"),
        ClassesRoots.AllUsersLabel => Strings.Get("Scope_AllUsers"),
        var group when Entry.Kind == EntryKind.WinX && group.StartsWith("Group", StringComparison.OrdinalIgnoreCase) =>
            Strings.Format("WinX_Group", group[5..]),
        var detail => detail
            .Replace(ClassesRoots.CurrentUserLabel, Strings.Get("Scope_CurrentUser"), StringComparison.Ordinal)
            .Replace(ClassesRoots.AllUsersLabel, Strings.Get("Scope_AllUsers"), StringComparison.Ordinal),
    };

    public bool ShowShiftBadge => Entry.ExtendedOnly;

    public bool CanToggle => Entry.Can(EntryCapabilities.Toggle);

    public bool CanRename => Entry.Can(EntryCapabilities.Rename);

    public bool CanDelete => Entry.Can(EntryCapabilities.Delete);

    public bool IsRegistryEntry => RegistryPath.TryParse(Entry.Location, out _);

    public string Glyph => Entry.Kind switch
    {
        EntryKind.ShellVerb => "",
        EntryKind.ShellExtension => "",
        EntryKind.ShellNew => "",
        EntryKind.WinX => "",
        EntryKind.PackagedApp => "",
        _ => "",
    };

    public string KindLabel => Strings.Get($"Kind_{Entry.Kind}");

    /// <summary>Bound two-way to the toggle switch. Setting it asks the owner to apply the change.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value || _isApplying)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
            _ = ApplyAsync(value);
        }
    }

    private async Task ApplyAsync(bool enabled)
    {
        _isApplying = true;
        try
        {
            if (!await _owner.SetStateAsync(this, enabled))
            {
                // Put the switch back when the change failed or was refused.
                _isEnabled = !enabled;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
        finally
        {
            _isApplying = false;
        }
    }

    [RelayCommand]
    private Task RenameAsync() => _owner.RenameAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);

    [RelayCommand]
    private void OpenLocation() => _owner.OpenLocation(this);

    [RelayCommand]
    private void CopyLocation() => SystemActions.CopyToClipboard(Entry.Location);
}
