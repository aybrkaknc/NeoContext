using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using NeoContext.App.Services;
using NeoContext.Core.Models;
using NeoContext.Core.Results;
using NeoContext.Core.Rules;
using NeoContext.Registry;
using NeoContext.Shell;
using NeoContext.Updates;

namespace NeoContext.App.ViewModels;

/// <summary>What the notice bar's button does.</summary>
public enum NoticeAction
{
    None,
    RestartExplorer,
    RestartElevated,
    TakeOwnership,
    OpenRelease,
    UndoDelete,
}

/// <summary>One labelled line in the entry details overlay.</summary>
public sealed record DetailRow(string Label, string Value);

/// <summary>A group button on the "File types" page, with its translated name.</summary>
public sealed record FileTypeChoice(FileTypeGroup Group, string Label);

/// <summary>
/// State and commands for the entry pages. All registry and file work happens on a background
/// thread; results come back as <see cref="Result"/> values and are turned into notices here.
/// </summary>
public sealed partial class MainViewModel(AppHost host, IDialogService dialogs) : ObservableObject
{
    private IReadOnlyList<ContextMenuEntry> _all = [];
    private Func<Result>? _retryAfterOwnership;
    private RegistryPath[] _ownershipTargets = [];
    private Uri? _releaseUrl;
    private string? _lastBackup;
    private string[] _extensions = [];
    private IReadOnlyList<string>? _knownExtensions;

    public IReadOnlyList<SectionGroup> SectionGroups => SectionItem.Groups;

    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    public bool IsElevated => SystemActions.IsElevated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionTitle), nameof(SectionDescription), nameof(IsFileTypesPage), nameof(EmptyMessage), nameof(CanCreate), nameof(IsWinXPage), nameof(IsCustomPathPage))]
    public partial SectionItem? SelectedSection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(SectionTitle), nameof(SectionDescription), nameof(EmptyMessage))]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsNoticeOpen { get; set; }

    [ObservableProperty]
    public partial bool IsDetailsOpen { get; set; }

    [ObservableProperty]
    public partial string DetailsTitle { get; set; } = "";

    public ObservableCollection<DetailRow> DetailRows { get; } = [];

    private string _detailsLocation = "";

    /// <summary>The entry being edited, or null when the edit form is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditOpen))]
    public partial EntryEditViewModel? Editing { get; set; }

    public bool IsEditOpen => Editing is not null;

    /// <summary>The "New item" form, or null when it is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewOpen))]
    public partial NewItemViewModel? Creating { get; set; }

    public bool IsNewOpen => Creating is not null;

    /// <summary>Only the pages that map to registry classes can take a new verb or handler.</summary>
    public bool CanCreate => SelectedSection is { } section
        && (ShellScopes.Keys.ContainsKey(section.Section) || section.Section is MenuSection.SendTo or MenuSection.OpenWith or MenuSection.WinX or MenuSection.GuidBlocked);

    public bool IsWinXPage => SelectedSection?.Section == MenuSection.WinX;

    public bool IsCustomPathPage => SelectedSection?.Section == MenuSection.CustomPath;

    [RelayCommand]
    private void ChooseCustomPath() =>
        Targeting = new NewTargetViewModel(MenuSection.CustomPath) { Target = host.Settings.CustomRegistryPath };

    public IconPickerViewModel IconPicker { get; } = new();

    [ObservableProperty]
    public partial bool IsIconPickerOpen { get; set; }

    [ObservableProperty]
    public partial string NoticeTitle { get; set; } = "";

    [ObservableProperty]
    public partial string NoticeMessage { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity NoticeSeverity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoticeAction), nameof(NoticeActionText))]
    public partial NoticeAction PendingAction { get; set; }

    public bool HasNoticeAction => PendingAction != NoticeAction.None;

    public string NoticeActionText => PendingAction == NoticeAction.None ? "" : Strings.Get($"Action_{PendingAction}");

    /// <summary>True while the search box has text; the list then covers every section.</summary>
    public bool IsSearching => EntryFilter.IsSearch(SearchText);

    public string SectionTitle => IsSearching ? Strings.Get("Search_Title") : SelectedSection?.Title ?? "";

    public string SectionDescription => IsSearching
        ? Strings.Format("Search_Description", Entries.Count)
        : IsCustomPathPage && host.Settings.CustomRegistryPath.Length > 0
            ? Strings.Format("CustomPath_Current", host.Settings.CustomRegistryPath)
            : SelectedSection?.Description ?? "";

    public string EmptyMessage => Strings.Get(
        IsSearching ? "Empty_Search"
        : IsFileTypesPage && _extensions.Length == 0 ? "Empty_FileTypes"
        : IsCustomPathPage && host.Settings.CustomRegistryPath.Length == 0 ? "Empty_CustomPath"
        : "Empty_Page");

    /// <summary>True on the "File types" page, which shows the extension picker.</summary>
    public bool IsFileTypesPage => SelectedSection?.Section == MenuSection.FileTypes;

    public IReadOnlyList<FileTypeChoice> FileTypeChoices { get; } =
        [.. FileTypeGroups.All.Select(g => new FileTypeChoice(g, Strings.Get($"FileType_{g.Name}")))];

    public bool HasSelectedExtensions => _extensions.Length > 0;

    public string FileTypeSummary => _extensions.Length == 0 ? "" : Strings.Format("FileType_Scanning", string.Join(", ", _extensions));

    [RelayCommand]
    private Task SelectFileTypeGroupAsync(FileTypeChoice choice) => SetExtensionsAsync([.. choice.Group.Extensions]);

    [RelayCommand]
    private Task ClearExtensionsAsync() => SetExtensionsAsync([]);

    /// <summary>Handles typed text such as "mp4", ".MP4" or "mp4, mkv".</summary>
    internal Task SubmitExtensionsAsync(string? text)
    {
        var parsed = (text ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(FileTypeGroups.NormalizeExtension)
            .ToList();
        if (parsed.Count == 0 || parsed.Any(e => e is null))
        {
            ShowNotice(InfoBarSeverity.Warning, Strings.Get("FileType_Invalid_Title"), Strings.Get("FileType_Invalid_Message"), NoticeAction.None);
            return Task.CompletedTask;
        }

        return SetExtensionsAsync([.. parsed.Select(e => e!).Distinct()]);
    }

    /// <summary>Registered extensions that start with what the user typed, for the suggestion list.</summary>
    internal IReadOnlyList<string> SuggestExtensions(string? text)
    {
        _knownExtensions ??= host.FileTypes.KnownExtensions();
        var typed = text?.Trim().TrimStart('*').TrimStart('.') ?? "";
        return typed.Length == 0
            ? []
            : [.. _knownExtensions.Where(e => e.StartsWith("." + typed, StringComparison.OrdinalIgnoreCase)).Take(12)];
    }

    private async Task SetExtensionsAsync(string[] extensions)
    {
        _extensions = extensions;
        OnPropertyChanged(nameof(HasSelectedExtensions));
        OnPropertyChanged(nameof(FileTypeSummary));
        OnPropertyChanged(nameof(EmptyMessage));
        await RefreshAsync();
    }

    partial void OnSelectedSectionChanged(SectionItem? value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var extensions = _extensions;
            _all = await Task.Run(() =>
            {
                IReadOnlyList<ContextMenuEntry> entries = host.Menus.Refresh();
                if (extensions.Length > 0)
                {
                    entries = [.. entries, .. host.FileTypes.Scan(extensions)];
                }

                return entries;
            });
            foreach (var error in host.Menus.LoadErrors)
            {
                AppLog.Warn($"Load failed: {error}");
            }

            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }

        ShowWelcomeOnce();
    }

    /// <summary>A short orientation shown on the very first launch only.</summary>
    private void ShowWelcomeOnce()
    {
        if (host.Settings.WelcomeShown)
        {
            return;
        }

        host.UpdateSettings(s => s with { WelcomeShown = true });
        if (!IsNoticeOpen)
        {
            ShowNotice(InfoBarSeverity.Informational, Strings.Get("Notice_Welcome_Title"), Strings.Get("Notice_Welcome_Message"), NoticeAction.None);
        }
    }

    private void ApplyFilter()
    {
        // A search looks through every section, so each row also shows which one it belongs to.
        var searching = IsSearching;
        var rows = EntryFilter.Apply(_all, SearchText, SelectedSection?.Section)
            .Select(entry => new EntryViewModel(entry, this, showSection: searching, query: searching ? SearchText : ""))
            .ToList();

        // After a toggle the same rows come back in the same order. Replacing them one by one keeps the list's
        // scroll position; clearing the list would jump back to the top.
        if (rows.Count == Entries.Count && rows.Select((row, i) => row.Entry.Id == Entries[i].Entry.Id).All(same => same))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                Entries[i] = rows[i];
            }
        }
        else
        {
            Entries.Clear();
            foreach (var row in rows)
            {
                Entries.Add(row);
            }
        }

        IsEmpty = Entries.Count == 0;
        OnPropertyChanged(nameof(SectionDescription));
    }

    /// <summary>Applies a toggle. Returns false when the switch must flip back.</summary>
    internal async Task<bool> SetStateAsync(EntryViewModel item, bool enabled)
    {
        var entry = item.Entry;
        var state = enabled ? EntryState.Enabled : EntryState.Disabled;

        if (!enabled && host.Menus.IsProtected(entry))
        {
            ShowError(new Error(ErrorKind.Protected, entry.DisplayName), entry);
            return false;
        }

        var result = await RunBusyAsync(() => host.Menus.SetState(entry, state));
        return await CompleteAsync(result, entry, () => host.Menus.SetState(entry, state));
    }

    internal Task ShowDetailsAsync(EntryViewModel item)
    {
        var entry = item.Entry;
        var rows = new List<(string, string)>
        {
            (Strings.Get("Details_Does"), item.DescribeEntry()),
            (Strings.Get("Details_Kind"), item.KindLabel),
            (Strings.Get("Details_Menu"), item.SectionLabel),
            (Strings.Get("Details_Status"), Strings.Get(entry.IsEnabled ? "Details_On" : "Details_Off")),
        };

        if (entry.Publisher is { } publisher)
        {
            rows.Add((Strings.Get("Details_Publisher"), publisher));
        }

        if (host.Menus.IsProtected(entry))
        {
            rows.Add((Strings.Get("Details_Protected"), Strings.Get("Details_Protected_Text")));
        }

        rows.Add((Strings.Get("Details_Effect"), Strings.Get($"Effect_{entry.Kind}")));

        if (!string.IsNullOrEmpty(item.Detail))
        {
            rows.Add((Strings.Get("Details_Scope"), item.Detail));
        }

        if (!string.IsNullOrWhiteSpace(entry.Command))
        {
            rows.Add((Strings.Get("Details_Command"), entry.Command));
        }

        rows.Add((Strings.Get("Details_Location"), entry.Location));
        if (entry.Kind is EntryKind.ShellExtension or EntryKind.PackagedApp)
        {
            rows.Add((Strings.Get("Details_Key"), entry.Key));
        }

        DetailsTitle = entry.DisplayName;
        _detailsLocation = entry.Location;
        DetailRows.Clear();
        foreach (var (label, value) in rows)
        {
            DetailRows.Add(new DetailRow(label, value));
        }

        IsDetailsOpen = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void CloseDetails() => IsDetailsOpen = false;

    [RelayCommand]
    private void CopyDetailsLocation() => SystemActions.CopyToClipboard(_detailsLocation);

    internal void BeginEdit(EntryViewModel item) => Editing = new EntryEditViewModel(item.Entry);

    internal async Task MoveAsync(EntryViewModel item, int direction)
    {
        var entry = item.Entry;
        var result = await RunBusyAsync(() => host.Menus.Move(entry, direction));
        await CompleteAsync(result, entry, () => host.Menus.Move(entry, direction));
    }

    /// <summary>Puts Windows' factory Win+X menu back, after a confirmation. The current menu is copied to the backups first.</summary>
    [RelayCommand]
    private async Task RestoreWinXDefaultsAsync()
    {
        if (!await dialogs.ConfirmAsync(Strings.Get("WinXRestore_Title"), Strings.Format("WinXRestore_Message", AppInfo.RegistryBackupDirectory), Strings.Get("WinXRestore_Confirm"), destructive: true))
        {
            return;
        }

        var result = await RunBusyAsync(() => host.WinX.RestoreDefaults(AppInfo.RegistryBackupDirectory));
        if (!result.IsSuccess)
        {
            AppLog.Warn($"Could not restore the Win+X menu: {result.Error}");
            ShowNotice(InfoBarSeverity.Error, Strings.Get($"Error_{result.Error!.Kind}"), Describe(result.Error), NoticeAction.None);
            return;
        }

        await RefreshAsync();
        ShowNotice(InfoBarSeverity.Informational, Strings.Get("Notice_RestartExplorer_Title"), Strings.Get("Notice_RestartExplorer_Message"), NoticeAction.RestartExplorer);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsIconPickerOpen = false;
        Editing = null;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (Editing is not { } editing || editing.ToEdit() is not { } edit)
        {
            return;
        }

        var entry = editing.Entry;
        Editing = null;
        var result = await RunBusyAsync(() => host.Menus.Edit(entry, edit));
        await CompleteAsync(result, entry, () => host.Menus.Edit(entry, edit));
    }

    [RelayCommand]
    private async Task OpenIconPickerAsync()
    {
        IsIconPickerOpen = true;
        if (IconPicker.Items.Count == 0)
        {
            await IconPicker.LoadAsync("shell32.dll");
        }
    }

    [RelayCommand]
    private void CloseIconPicker() => IsIconPickerOpen = false;

    internal Task LoadIconFileAsync(string file) => IconPicker.LoadAsync(file);

    internal void ChooseIcon(IconPickItem item)
    {
        // The picker serves whichever form is open: the new-item form or the edit form.
        if (Creating is { } creating)
        {
            creating.Icon = IconPicker.Compose(item);
        }
        else if (Editing is { } editing)
        {
            editing.Icon = IconPicker.Compose(item);
        }

        IsIconPickerOpen = false;
    }

    [RelayCommand]
    private void BeginNew()
    {
        if (!CanCreate || SelectedSection is not { } section)
        {
            return;
        }

        // Shortcut pages ask for a target; registry pages ask for a verb or a handler.
        if (section.Section == MenuSection.GuidBlocked)
        {
            Creating = new NewItemViewModel(section.Section) { HandlerOnly = true, TypeIndex = 1 };
        }
        else if (section.Section == MenuSection.WinX)
        {
            var groups = host.WinX.GroupNames()
                .Select(name => new GroupOption(name, Strings.Format("WinX_Group", name.StartsWith("Group", StringComparison.OrdinalIgnoreCase) ? name[5..] : name)))
                .ToList();
            Targeting = new NewTargetViewModel(section.Section, groups);
        }
        else if (section.Section is MenuSection.SendTo or MenuSection.OpenWith)
        {
            Targeting = new NewTargetViewModel(section.Section);
        }
        else
        {
            Creating = new NewItemViewModel(section.Section);
        }
    }

    [RelayCommand]
    private void CancelNew()
    {
        IsIconPickerOpen = false;
        Creating = null;
    }

    [RelayCommand]
    private async Task PasteGuidAsync()
    {
        if (Creating is { } creating && await SystemActions.ReadClipboardTextAsync() is { } text)
        {
            creating.GuidText = text.Trim();
        }
    }

    [RelayCommand]
    private async Task SaveNewAsync()
    {
        if (Creating is not { } creating)
        {
            return;
        }

        creating.Error = "";
        Func<Result> create;
        string name;
        if (creating.IsHandler)
        {
            if (creating.ToGuid() is not { } clsid)
            {
                return;
            }

            name = clsid.ToString("B");
            if (creating.Section == MenuSection.GuidBlocked)
            {
                // Blocking a CLSID that is not installed yet is fine, so it is not checked.
                create = () => host.Blocked.Block(clsid);
            }
            else
            {
                if (!host.Creator.ClsidExists(clsid))
                {
                    creating.Error = Strings.Get("New_ClsidMissing");
                    return;
                }

                create = () => host.Creator.CreateHandler(creating.Section, clsid, creating.AllUsers);
            }
        }
        else
        {
            if (creating.ToVerb() is not { } verb)
            {
                return;
            }

            name = verb.Name;
            create = () => host.Creator.CreateVerb(creating.Section, verb, creating.AllUsers);
        }

        Creating = null;
        await RunCreateAsync(create, name);
    }

    /// <summary>Runs a creation off the UI thread, then shows the outcome and reloads the list.</summary>
    private async Task RunCreateAsync(Func<Result> create, string name)
    {
        var result = await RunBusyAsync(create);
        if (!result.IsSuccess)
        {
            AppLog.Warn($"Could not create {name}: {result.Error}");
            var action = result.Error!.Kind == ErrorKind.RequiresElevation ? NoticeAction.RestartElevated : NoticeAction.None;
            ShowNotice(InfoBarSeverity.Error, Strings.Get($"Error_{result.Error.Kind}"), Describe(result.Error), action);
            return;
        }

        AppLog.Info($"Created {name}");
        await RefreshAsync();
        ShowNotice(InfoBarSeverity.Success, Strings.Get("Notice_Done_Title"), Strings.Format("Notice_Created_Message", name), NoticeAction.None);
    }

    /// <summary>The "New item" form for shortcut pages, or null when it is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetOpen))]
    public partial NewTargetViewModel? Targeting { get; set; }

    public bool IsTargetOpen => Targeting is not null;

    [RelayCommand]
    private void CancelTarget() => Targeting = null;

    [RelayCommand]
    private async Task SaveTargetAsync()
    {
        if (Targeting is not { } form || !form.Validate())
        {
            return;
        }

        if (form.Section == MenuSection.CustomPath)
        {
            host.UpdateSettings(s => s with { CustomRegistryPath = form.Target.Trim() });
            Targeting = null;
            await RefreshAsync();
            OnPropertyChanged(nameof(SectionDescription));
            OnPropertyChanged(nameof(EmptyMessage));
            return;
        }

        var target = form.Target.Trim().Trim('"');
        var group = form.GroupName;
        var name = string.IsNullOrWhiteSpace(form.Name) ? Path.GetFileNameWithoutExtension(target) : form.Name.Trim();
        Func<Result> create = form.Section switch
        {
            MenuSection.SendTo => () => host.SendTo.CreateShortcut(form.Name, target, form.Arguments),
            MenuSection.OpenWith => () => host.OpenWith.CreateApp(target, form.Name, form.AllUsers),
            MenuSection.WinX => () => host.WinX.CreateShortcut(group!, form.Name, target, form.Arguments),
            _ => () => Result.Failure(ErrorKind.NotSupported, $"Nothing can be added to {form.Section}."),
        };

        Targeting = null;
        await RunCreateAsync(create, name);
    }

    internal async Task RenameAsync(EntryViewModel item)
    {
        var name = await dialogs.PromptForNameAsync(Strings.Get("Rename_Title"), item.Name);
        if (name is null || name == item.Name)
        {
            return;
        }

        var entry = item.Entry;
        var result = await RunBusyAsync(() => host.Menus.Rename(entry, name));
        await CompleteAsync(result, entry, () => host.Menus.Rename(entry, name));
    }

    internal async Task DeleteAsync(EntryViewModel item)
    {
        var entry = item.Entry;

        // Refuse before asking for confirmation or writing a backup.
        if (host.Menus.IsProtected(entry))
        {
            ShowError(new Error(ErrorKind.Protected, entry.DisplayName), entry);
            return;
        }

        var isRegistry = RegistryPath.TryParse(entry.Location, out var path);
        var message = Strings.Format(isRegistry ? "Delete_Message_Registry" : "Delete_Message_File", entry.DisplayName, AppInfo.RegistryBackupDirectory);
        if (!await dialogs.ConfirmAsync(Strings.Get("Delete_Title"), message, Strings.Get("Delete_Confirm"), destructive: true))
        {
            return;
        }

        string? backupFile = null;
        Result Delete()
        {
            // Registry entries are exported first; without a backup nothing is deleted.
            if (isRegistry)
            {
                var backup = host.Exporter.Export(path);
                if (!backup.IsSuccess)
                {
                    return backup.ToResult();
                }

                backupFile = backup.Value;
                AppLog.Info($"Backed up {path} to {backup.Value}");
            }

            return host.Menus.Delete(entry);
        }

        var result = await RunBusyAsync(Delete);
        var done = await CompleteAsync(result, entry, Delete);

        // Offer to undo when the entry was backed up and no Explorer restart notice is showing.
        if (done && backupFile is not null && !host.Menus.RequiresExplorerRestart(entry))
        {
            _lastBackup = backupFile;
            ShowNotice(InfoBarSeverity.Success, Strings.Get("Notice_Done_Title"), Strings.Format("Notice_Deleted_Message", entry.DisplayName), NoticeAction.UndoDelete);
        }
    }

    private async Task UndoDeleteAsync()
    {
        var file = _lastBackup;
        if (file is null)
        {
            return;
        }

        var result = await RunBusyAsync(() => RegistryExporter.Import(file));
        if (!result.IsSuccess)
        {
            AppLog.Warn($"Undo failed: {result.Error}");
            ShowNotice(InfoBarSeverity.Error, Strings.Get($"Error_{result.Error!.Kind}"), Describe(result.Error), NoticeAction.None);
            return;
        }

        _lastBackup = null;
        await RefreshAsync();
        ShowNotice(InfoBarSeverity.Success, Strings.Get("Notice_Done_Title"), Strings.Get("Notice_Restored_Message"), NoticeAction.None);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    /// <summary>Runs registry or file work off the UI thread while the busy ring is shown.</summary>
    private async Task<Result> RunBusyAsync(Func<Result> work)
    {
        IsBusy = true;
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>A translated summary of the failure, followed by the technical detail.</summary>
    private static string Describe(Error error)
    {
        var summary = Strings.Get($"Error_{error.Kind}_Message");

        // The technical detail is English and only useful when the cause is unclear.
        return error.Kind is ErrorKind.Unexpected or ErrorKind.AccessDenied or ErrorKind.RequiresElevation
            ? $"{summary}{Environment.NewLine}{error.Message}"
            : summary;
    }

    internal void OpenLocation(EntryViewModel item)
    {
        if (RegistryPath.TryParse(item.Location, out var path))
        {
            SystemActions.OpenInRegistryEditor(path);
        }
        else
        {
            SystemActions.ShowInExplorer(item.Location);
        }
    }

    /// <summary>Shows the outcome of a change and reloads the list.</summary>
    private async Task<bool> CompleteAsync(Result result, ContextMenuEntry entry, Func<Result> retry)
    {
        if (!result.IsSuccess)
        {
            AppLog.Warn($"{entry.Id}: {result.Error}");
            ShowError(result.Error!, entry, retry);
            return false;
        }

        AppLog.Info($"Changed {entry.Id}");
        if (host.Menus.RequiresExplorerRestart(entry))
        {
            ShowNotice(InfoBarSeverity.Informational, Strings.Get("Notice_RestartExplorer_Title"), Strings.Get("Notice_RestartExplorer_Message"), NoticeAction.RestartExplorer);
        }
        else
        {
            ShowNotice(InfoBarSeverity.Success, Strings.Get("Notice_Done_Title"), Strings.Format("Notice_Done_Message", entry.DisplayName), NoticeAction.None);
        }

        await RefreshAsync();
        return true;
    }

    private void ShowError(Error error, ContextMenuEntry entry, Func<Result>? retry = null)
    {
        var action = error.Kind switch
        {
            ErrorKind.RequiresElevation => NoticeAction.RestartElevated,
            ErrorKind.AccessDenied when retry is not null && RegistryPath.TryParse(entry.Location, out _) => NoticeAction.TakeOwnership,
            _ => NoticeAction.None,
        };

        if (action == NoticeAction.TakeOwnership)
        {
            var key = RegistryPath.Parse(entry.Location);

            // Only moving a key (ShellNew and shell extension toggles) also needs rights on its parent.
            // Changing values or deleting a verb's own key does not, so the parent is left alone.
            var moves = entry.Kind is EntryKind.ShellNew or EntryKind.ShellExtension;
            _ownershipTargets = moves && key.Parent is { } parent ? [key, parent] : [key];
            _retryAfterOwnership = retry;
        }

        var message = error.Kind is ErrorKind.Protected
            ? Strings.Format("Error_Protected_Message", entry.DisplayName)
            : Describe(error);
        ShowNotice(InfoBarSeverity.Error, Strings.Get($"Error_{error.Kind}"), message, action);
    }

    private void ShowNotice(InfoBarSeverity severity, string title, string message, NoticeAction action)
    {
        NoticeSeverity = severity;
        NoticeTitle = title;
        NoticeMessage = message;
        PendingAction = action;
        IsNoticeOpen = true;
    }

    /// <summary>Called after the start-up update check.</summary>
    public void ShowUpdateAvailable(ReleaseInfo release)
    {
        _releaseUrl = release.PageUrl;
        ShowNotice(InfoBarSeverity.Success, Strings.Get("Notice_Update_Title"), Strings.Format("Notice_Update_Message", release.Version), NoticeAction.OpenRelease);
    }

    public void ShowDictionariesUpdated(int count) =>
        ShowNotice(InfoBarSeverity.Success, Strings.Get("Notice_Dictionaries_Title"), Strings.Format("Notice_Dictionaries_Message", count), NoticeAction.None);

    [RelayCommand]
    private async Task RunNoticeActionAsync()
    {
        var action = PendingAction;
        IsNoticeOpen = false;
        PendingAction = NoticeAction.None;

        switch (action)
        {
            case NoticeAction.RestartExplorer:
                await RestartExplorerAsync();
                break;

            case NoticeAction.RestartElevated:
                if (SystemActions.RestartElevated())
                {
                    Microsoft.UI.Xaml.Application.Current.Exit();
                }
                else
                {
                    // The user declined the Windows permission prompt.
                    ShowNotice(InfoBarSeverity.Warning, Strings.Get("Notice_ElevationCancelled_Title"), Strings.Get("Notice_ElevationCancelled_Message"), NoticeAction.RestartElevated);
                }

                break;

            case NoticeAction.TakeOwnership:
                await TakeOwnershipAndRetryAsync();
                break;

            case NoticeAction.UndoDelete:
                await UndoDeleteAsync();
                break;

            case NoticeAction.OpenRelease when _releaseUrl is not null:
                SystemActions.OpenUrl(_releaseUrl.ToString());
                break;
        }
    }

    private async Task RestartExplorerAsync()
    {
        // Restarting closes every open Explorer window and the taskbar for a moment, so ask first.
        if (!await dialogs.ConfirmAsync(Strings.Get("Explorer_Title"), Strings.Get("Explorer_Message"), Strings.Get("Explorer_Confirm")))
        {
            ShowNotice(InfoBarSeverity.Informational, Strings.Get("Notice_RestartExplorer_Title"), Strings.Get("Notice_RestartExplorer_Message"), NoticeAction.RestartExplorer);
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(ExplorerRestarter.Restart);
            ShowNotice(InfoBarSeverity.Success, Strings.Get("Notice_Done_Title"), Strings.Get("Explorer_Done"), NoticeAction.None);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            AppLog.Warn($"Explorer restart failed: {ex.Message}");
            ShowNotice(InfoBarSeverity.Error, Strings.Get("Error_Unexpected"), Strings.Format("Explorer_Failed", ex.Message), NoticeAction.RestartExplorer);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TakeOwnershipAndRetryAsync()
    {
        var targets = _ownershipTargets;
        var retry = _retryAfterOwnership;
        if (retry is null || targets.Length == 0)
        {
            return;
        }

        var message = Strings.Format("Ownership_Message", Environment.NewLine, string.Join(Environment.NewLine, targets.Select(t => t.ToString())));
        if (!await dialogs.ConfirmAsync(Strings.Get("Ownership_Title"), message, Strings.Get("Ownership_Confirm")))
        {
            return;
        }

        foreach (var target in targets)
        {
            var taken = await Task.Run(() => host.Ownership.TakeOwnership(target));
            if (!taken.IsSuccess)
            {
                ShowNotice(InfoBarSeverity.Error, Strings.Get("Error_AccessDenied"), Describe(taken.Error!), NoticeAction.None);
                return;
            }

            AppLog.Info($"Took ownership of {target}; previous owner saved.");
        }

        var result = await Task.Run(retry);
        if (result.IsSuccess)
        {
            await RefreshAsync();
            ShowNotice(InfoBarSeverity.Success, Strings.Get("Ownership_Done_Title"), Strings.Get("Ownership_Done_Message"), NoticeAction.None);
        }
        else
        {
            ShowNotice(InfoBarSeverity.Error, Strings.Get($"Error_{result.Error!.Kind}"), Describe(result.Error), NoticeAction.None);
        }
    }
}
