using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeoContext.App.Services;
using NeoContext.Updates;

namespace NeoContext.App.ViewModels;

/// <summary>A language choice; an empty tag means "follow Windows".</summary>
public sealed record LanguageOption(string Tag, string Name)
{
    public override string ToString() => Name;
}

/// <summary>The settings page: preferences, update check, restoring permissions and about.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly IDialogService _dialogs;
    private ReleaseInfo? _latest;

    public SettingsViewModel(AppHost host, IDialogService dialogs)
    {
        _host = host;
        _dialogs = dialogs;
        Languages =
        [
            new LanguageOption("", Strings.Get("Settings_Language_System")),
            new LanguageOption("en-US", "English"),
            new LanguageOption("tr-TR", "Türkçe"),
        ];

        SelectedLanguage = Languages.FirstOrDefault(l => l.Tag == host.Settings.Language) ?? Languages[0];
        ProtectEssentialEntries = host.Settings.ProtectEssentialEntries;
        CheckForUpdates = host.Settings.CheckForUpdates;
        UpdateStatus = Strings.Format("Settings_Update_Current", AppInfo.Version);
        RefreshOwnershipBackups();
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public string VersionText => Strings.Format("Settings_About_Version", AppInfo.Version, Strings.Get("Settings_About_Portable"));

    public string DataDirectory => AppInfo.DataDirectory;

    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool LanguageChanged { get; set; }

    [ObservableProperty]
    public partial bool ProtectEssentialEntries { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingForUpdates { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateAvailable { get; set; }

    [ObservableProperty]
    public partial int OwnershipBackupCount { get; set; }

    [ObservableProperty]
    public partial string OwnershipStatus { get; set; } = "";

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value.Tag == _host.Settings.Language)
        {
            return;
        }

        _host.UpdateSettings(s => s with { Language = value.Tag });
        LanguageChanged = true;
    }

    partial void OnProtectEssentialEntriesChanged(bool value) =>
        _host.UpdateSettings(s => s with { ProtectEssentialEntries = value });

    partial void OnCheckForUpdatesChanged(bool value) =>
        _host.UpdateSettings(s => s with { CheckForUpdates = value });

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        IsCheckingForUpdates = true;
        try
        {
            var result = await _host.UpdateChecker.CheckAsync();
            if (!result.IsSuccess)
            {
                UpdateStatus = Strings.Format("Settings_Update_Failed", result.Error!.Message);
                return;
            }

            _host.UpdateSettings(s => s with { LastUpdateCheck = DateTimeOffset.UtcNow });
            _latest = result.Value.Latest;
            IsUpdateAvailable = _latest is not null;
            UpdateStatus = _latest is null
                ? Strings.Format("Settings_Update_UpToDate", AppInfo.Version)
                : Strings.Format("Settings_Update_Available", _latest.Version);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// Downloads the portable zip for this architecture and checks it against the SHA-256 digest
    /// published with the release before keeping it.
    /// </summary>
    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (_latest is null)
        {
            return;
        }

        const string extension = ".zip";
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var asset = _latest.Assets.FirstOrDefault(a => a.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase))
                    ?? _latest.Assets.FirstOrDefault(a => a.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
        {
            SystemActions.OpenUrl(_latest.PageUrl.ToString());
            return;
        }

        IsCheckingForUpdates = true;
        UpdateStatus = Strings.Format("Settings_Update_Downloading", asset.Name);
        try
        {
            var policy = SignaturePolicy.NotRequired;
            var result = await _host.UpdateDownloader.DownloadAsync(asset, AppInfo.DownloadDirectory, policy);
            if (result.IsSuccess)
            {
                UpdateStatus = Strings.Format("Settings_Update_Downloaded", asset.Name);
                SystemActions.ShowInExplorer(result.Value);
            }
            else
            {
                AppLog.Warn($"Update download rejected: {result.Error}");
                UpdateStatus = Strings.Format("Settings_Update_Rejected", result.Error!.Message);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage() => SystemActions.OpenUrl(_latest?.PageUrl.ToString() ?? $"{AppInfo.RepositoryUrl}/releases");

    [RelayCommand]
    private async Task RestorePermissionsAsync()
    {
        if (_host.Ownership.GetBackups().Count == 0)
        {
            OwnershipStatus = Strings.Get("Settings_Ownership_None");
            return;
        }

        var count = _host.Ownership.GetBackups().Count;
        if (!await _dialogs.ConfirmAsync(Strings.Get("Restore_Title"), Strings.Format("Restore_Message", count), Strings.Get("Restore_Confirm")))
        {
            return;
        }

        var failures = 0;
        foreach (var backup in _host.Ownership.GetBackups())
        {
            var result = await Task.Run(() => _host.Ownership.Restore(backup));
            if (!result.IsSuccess)
            {
                failures++;
                AppLog.Warn($"Restore failed: {result.Error}");
            }
        }

        RefreshOwnershipBackups();
        OwnershipStatus = failures == 0 ? Strings.Get("Settings_Ownership_Restored") : Strings.Format("Settings_Ownership_Failed", failures);
    }

    [RelayCommand]
    private void OpenDataFolder() => SystemActions.ShowInExplorer(AppInfo.DataDirectory);

    [RelayCommand]
    private void OpenRepository() => SystemActions.OpenUrl(AppInfo.RepositoryUrl);

    [RelayCommand]
    private void OpenOriginalProject() => SystemActions.OpenUrl(AppInfo.OriginalProjectUrl);

    private void RefreshOwnershipBackups()
    {
        OwnershipBackupCount = _host.Ownership.GetBackups().Count;
        OwnershipStatus = Strings.Format("Settings_Ownership_Count", OwnershipBackupCount);
    }
}
