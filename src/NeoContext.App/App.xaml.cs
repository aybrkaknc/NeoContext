using Microsoft.UI.Xaml;
using NeoContext.App.Services;
using NeoContext.App.Views;
using NeoContext.Updates;

namespace NeoContext.App;

/// <summary>Start-up: error handling, language, services, main window, background update check.</summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InstallGlobalErrorHandlers();
        InitializeComponent();
    }

    public static AppHost Host { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Host = new AppHost();
        ApplyLanguage(Host.Settings.Language);
        AppLog.Info($"NeoContext {AppInfo.Version} started (packaged: {AppInfo.IsPackaged}, elevated: {SystemActions.IsElevated}).");

        _window = new MainWindow(Host);
        _window.Activate();

        _ = CheckForUpdatesInBackgroundAsync();
    }

    /// <summary>The language must be set before any UI is created; changing it needs a restart.</summary>
    private static void ApplyLanguage(string language)
    {
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Warn($"Could not apply language '{language}': {ex.Message}");
        }
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        var settings = Host.Settings;
        if (!settings.CheckForUpdates || !UpdateSchedule.IsDue(settings.LastUpdateCheck, settings.UpdateCheckIntervalDays, DateTimeOffset.UtcNow))
        {
            return;
        }

        var result = await Host.UpdateChecker.CheckAsync();
        if (!result.IsSuccess)
        {
            AppLog.Warn($"Update check failed: {result.Error}");
            return;
        }

        Host.UpdateSettings(s => s with { LastUpdateCheck = DateTimeOffset.UtcNow });
        if (result.Value.Latest is { } latest)
        {
            _window?.ShowUpdateAvailable(latest);
        }
    }

    /// <summary>Unexpected exceptions are logged; UI-thread ones are shown instead of closing the app.</summary>
    private void InstallGlobalErrorHandlers()
    {
        UnhandledException += (_, e) =>
        {
            AppLog.Error("Unhandled UI exception.", e.Exception);
            e.Handled = true;
            _window?.ShowUnexpectedError(e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("Unhandled exception.", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };
    }
}
