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
}

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

    public IReadOnlyList<SectionItem> Sections => SectionItem.All;

    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    public bool IsElevated => SystemActions.IsElevated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionTitle), nameof(SectionDescription))]
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
        : SelectedSection?.Description ?? "";

    public string EmptyMessage => Strings.Get(IsSearching ? "Empty_Search" : "Empty_Page");

    partial void OnSelectedSectionChanged(SectionItem? value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            _all = await Task.Run(host.Menus.Refresh);
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
    }

    private void ApplyFilter()
    {
        Entries.Clear();

        // A search looks through every section, so each row also shows which one it belongs to.
        var searching = IsSearching;
        foreach (var entry in EntryFilter.Apply(_all, SearchText, SelectedSection?.Section))
        {
            Entries.Add(new EntryViewModel(entry, this, showSection: searching));
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

        var result = await Task.Run(() => host.Menus.SetState(entry, state));
        return await CompleteAsync(result, entry, () => host.Menus.SetState(entry, state));
    }

    internal async Task RenameAsync(EntryViewModel item)
    {
        var name = await dialogs.PromptForNameAsync(Strings.Get("Rename_Title"), item.Name);
        if (name is null || name == item.Name)
        {
            return;
        }

        var entry = item.Entry;
        var result = await Task.Run(() => host.Menus.Rename(entry, name));
        await CompleteAsync(result, entry, () => host.Menus.Rename(entry, name));
    }

    internal async Task DeleteAsync(EntryViewModel item)
    {
        var entry = item.Entry;
        var isRegistry = RegistryPath.TryParse(entry.Location, out var path);
        var message = Strings.Format(isRegistry ? "Delete_Message_Registry" : "Delete_Message_File", entry.DisplayName, AppInfo.RegistryBackupDirectory);
        if (!await dialogs.ConfirmAsync(Strings.Get("Delete_Title"), message, Strings.Get("Delete_Confirm"), destructive: true))
        {
            return;
        }

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

                AppLog.Info($"Backed up {path} to {backup.Value}");
            }

            return host.Menus.Delete(entry);
        }

        var result = await Task.Run(Delete);
        await CompleteAsync(result, entry, Delete);
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
            : error.Message;
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

    [RelayCommand]
    private async Task RunNoticeActionAsync()
    {
        var action = PendingAction;
        IsNoticeOpen = false;
        PendingAction = NoticeAction.None;

        switch (action)
        {
            case NoticeAction.RestartExplorer:
                await Task.Run(ExplorerRestarter.Restart);
                break;

            case NoticeAction.RestartElevated:
                if (SystemActions.RestartElevated())
                {
                    Microsoft.UI.Xaml.Application.Current.Exit();
                }

                break;

            case NoticeAction.TakeOwnership:
                await TakeOwnershipAndRetryAsync();
                break;

            case NoticeAction.OpenRelease when _releaseUrl is not null:
                SystemActions.OpenUrl(_releaseUrl.ToString());
                break;
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
                ShowNotice(InfoBarSeverity.Error, Strings.Get("Error_AccessDenied"), taken.Error!.Message, NoticeAction.None);
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
            ShowNotice(InfoBarSeverity.Error, Strings.Get($"Error_{result.Error!.Kind}"), result.Error.Message, NoticeAction.None);
        }
    }
}
