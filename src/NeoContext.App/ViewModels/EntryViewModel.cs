using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NeoContext.App.Services;
using NeoContext.Core.Models;
using NeoContext.Core.Rules;
using NeoContext.Registry;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace NeoContext.App.ViewModels;

/// <summary>One row in the entry list. Changes are forwarded to <see cref="MainViewModel"/>.</summary>
public sealed partial class EntryViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _isEnabled;
    private bool _isApplying;

    public EntryViewModel(ContextMenuEntry entry, MainViewModel owner, bool showSection = false, string query = "")
    {
        Entry = entry;
        _owner = owner;
        _isEnabled = entry.IsEnabled;
        ShowSection = showSection;
        Query = query;
        _ = LoadIconAsync();
    }

    /// <summary>The program's own icon when it can be found; otherwise the row shows the kind glyph.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(NoIcon))]
    public partial ImageSource? IconSource { get; set; }

    public bool HasIcon => IconSource is not null;

    public bool NoIcon => IconSource is null;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<ImageSource?>> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private async Task LoadIconAsync()
    {
        if (ResolveIcon() is not { } icon)
        {
            return;
        }

        IconSource = await IconCache.GetOrAdd($"{icon.File}|{icon.Index}", _ => LoadImageAsync(icon.File, icon.Index));
    }

    /// <summary>The icon named by the entry, otherwise the one of the program in its command.</summary>
    private (string File, int Index)? ResolveIcon()
    {
        if (!string.IsNullOrWhiteSpace(Entry.IconLocation) && ParseIconLocation(Entry.IconLocation) is { } own)
        {
            return own;
        }

        if (Entry.Kind is EntryKind.ShellVerb or EntryKind.WinX
            && FileDescriptions.ExtractFile(Entry.Command) is { } program
            && IsIconFile(program))
        {
            return (program, 0);
        }

        return null;
    }

    /// <summary>Reads <c>"C:\app\x.exe",0</c> or <c>%SystemRoot%\system32\shell32.dll,-16</c>.</summary>
    private static (string File, int Index)? ParseIconLocation(string location)
    {
        var raw = Environment.ExpandEnvironmentVariables(location.Trim());
        var index = 0;
        var comma = raw.LastIndexOf(',');
        if (comma > 0 && int.TryParse(raw[(comma + 1)..].Trim(), out var parsed))
        {
            index = parsed;
            raw = raw[..comma];
        }

        var file = raw.Trim('"', ' ');
        if (!Path.IsPathRooted(file))
        {
            file = Path.Combine(Environment.SystemDirectory, file);
        }

        return IsIconFile(file) ? (file, index) : null;
    }

    private static bool IsIconFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".ico" or ".png" or ".dll" or ".icl" && File.Exists(path);

    private static async Task<ImageSource?> LoadImageAsync(string file, int index)
    {
        try
        {
            // A program's first icon, an .ico or a .png: Explorer's own thumbnail is the best quality.
            if (index == 0 && Path.GetExtension(file).ToLowerInvariant() is ".exe" or ".ico" or ".png")
            {
                var storage = await StorageFile.GetFileFromPathAsync(file);
                using var thumbnail = await storage.GetThumbnailAsync(ThumbnailMode.SingleItem, 32, ThumbnailOptions.UseCurrentScale);
                if (thumbnail is null)
                {
                    return null;
                }

                var image = new BitmapImage();
                await image.SetSourceAsync(thumbnail);
                return image;
            }

            // Any other icon (a DLL resource, or an index other than 0) is read from the file directly.
            var pixels = await Task.Run(() => IconExtractor.Extract(file, index));
            if (pixels is null)
            {
                return null;
            }

            return await IconImage.FromPixelsAsync(pixels);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FileNotFoundException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The search text, so the name can show where it matched.</summary>
    public string Query { get; }

    public ContextMenuEntry Entry { get; }

    /// <summary>Search results span every page, so they carry a tag naming the page the entry is on.</summary>
    public bool ShowSection { get; }

    /// <summary>True for entries NeoContext will not disable or delete while protection is on. Shown as a shield.</summary>
    public bool IsProtected => _owner.IsEntryProtected(Entry);

    public string ProtectedText => Strings.Get("Details_Protected_Text");

    public string SectionLabel => Strings.Get($"Section_{Entry.Section}");

    public string Name => Entry.DisplayName;

    /// <summary>
    /// The second line: what the entry does. Order: what the system reports (file description, COM class
    /// name), then the built-in description of well-known entries, then a kind-based sentence, and
    /// only as a last resort the command. The full path stays in the tooltip and the "more" menu.
    /// </summary>
    public string Secondary
    {
        get
        {
            var text = DescribeEntry();
            return Entry.Publisher is { } publisher && !text.Contains(publisher, StringComparison.OrdinalIgnoreCase)
                ? $"{text} · {publisher}"
                : text;
        }
    }

    /// <summary>What the entry does, without the publisher.</summary>
    public string DescribeEntry()
    {
        {
            if (Entry.Description is { } described)
            {
                return Strings.Format($"Desc_From_{Entry.Kind}", described);
            }

            if (Strings.TryGet($"Desc_Known_{Entry.Kind}_{Entry.Key.ToLowerInvariant()}") is { } known)
            {
                return known;
            }

            return Entry.Kind switch
            {
                EntryKind.ShellNew => Strings.Format("Desc_Generic_ShellNew", Entry.Key),
                EntryKind.PackagedApp when Entry.Detail is { } types => Strings.Format("Desc_Generic_PackagedApp", types),
                _ => Strings.Get($"Desc_Generic_{Entry.Kind}"),
            };
        }
    }

    public string Location => Entry.Location;

    /// <summary>Full location, plus the CLSID for COM-based entries.</summary>
    public string Tooltip => Entry.Kind is EntryKind.ShellExtension or EntryKind.PackagedApp
        ? $"{Entry.Location}{Environment.NewLine}{Entry.Key}"
        : Entry.Location;

    /// <summary>The short tag on the right: scope, extension, Win+X group or item types.</summary>
    /// <summary>"On" or "Off" next to the switch, so the state is readable without knowing what the colour means.</summary>
    public string StateText => IsEnabled ? Strings.Get("State_On") : Strings.Get("State_Off");

    public string Detail => Entry.Detail switch
    {
        null => "",

        // Scope ("All users" / "Current user") is not shown: it does not help the user decide anything.
        ClassesRoots.CurrentUserLabel => "",
        ClassesRoots.AllUsersLabel => "",
        var group when Entry.Kind == EntryKind.WinX && group.StartsWith("Group", StringComparison.OrdinalIgnoreCase) =>
            Strings.Format("WinX_Group", group[5..]),
        var detail => detail
            .Replace(ClassesRoots.CurrentUserLabel, Strings.Get("Scope_CurrentUser"), StringComparison.Ordinal)
            .Replace(ClassesRoots.AllUsersLabel, Strings.Get("Scope_AllUsers"), StringComparison.Ordinal),
    };

    public bool ShowShiftBadge => Entry.ExtendedOnly;

    public string ShiftTooltip => Strings.Get("ShiftBadge_Tooltip");

    public bool CanToggle => Entry.Can(EntryCapabilities.Toggle);

    public bool CanRename => Entry.Can(EntryCapabilities.Rename);

    public bool CanDelete => Entry.Can(EntryCapabilities.Delete);

    public bool CanEdit => Entry.Can(EntryCapabilities.Edit);

    public bool CanMove => Entry.Can(EntryCapabilities.Move);

    public bool IsRegistryEntry => RegistryPath.TryParse(Entry.Location, out _);

    public string Glyph => Entry.Kind switch
    {
        EntryKind.SendTo => "",
        EntryKind.OpenWith => "",
        EntryKind.Enhance => "",
        EntryKind.Detailed => "",
        EntryKind.DropEffect => "",
        EntryKind.GuidBlocked => "",
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
    private Task ShowDetailsAsync() => _owner.ShowDetailsAsync(this);

    [RelayCommand]
    private void Edit() => _owner.BeginEdit(this);

    [RelayCommand]
    private Task MoveUpAsync() => _owner.MoveAsync(this, -1);

    [RelayCommand]
    private Task MoveDownAsync() => _owner.MoveAsync(this, 1);

    [RelayCommand]
    private Task RenameAsync() => _owner.RenameAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);

    [RelayCommand]
    private void OpenLocation() => _owner.OpenLocation(this);

    [RelayCommand]
    private void CopyLocation() => SystemActions.CopyToClipboard(Entry.Location);
}
