using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoContext.App.Services;
using NeoContext.App.ViewModels;
using NeoContext.Updates;

namespace NeoContext.App.Views;

/// <summary>The only window. It switches between the entry pages and the settings page.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow(AppHost host)
    {
        ViewModel = new MainViewModel(host, new DialogService(() => Content.XamlRoot));
        InitializeComponent();

        Title = "NeoContext";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "NeoContext.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 480;
        }

        SettingsPanel.ViewModel = new SettingsViewModel(host);
        Navigation.SelectedItem = ViewModel.Sections[0];
        _ = ViewModel.RefreshAsync();
    }

    public MainViewModel ViewModel { get; }

    public void ShowUpdateAvailable(ReleaseInfo release) => DispatcherQueue.TryEnqueue(() => ViewModel.ShowUpdateAvailable(release));

    public void ShowUnexpectedError(Exception exception) => DispatcherQueue.TryEnqueue(() =>
    {
        ViewModel.NoticeSeverity = InfoBarSeverity.Error;
        ViewModel.NoticeTitle = Strings.Get("Error_Unexpected");
        ViewModel.NoticeMessage = exception.Message;
        ViewModel.PendingAction = NoticeAction.None;
        ViewModel.IsNoticeOpen = true;
    });

    /// <summary>x:Bind helper: show "nothing here" only when the list is empty and not loading.</summary>
    public Visibility ShowEmptyText(bool isEmpty, bool isBusy) => isEmpty && !isBusy ? Visibility.Visible : Visibility.Collapsed;

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var showSettings = args.IsSettingsSelected;
        SettingsPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        EntriesPanel.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;

        if (!showSettings && args.SelectedItem is SectionItem section)
        {
            ViewModel.SearchText = "";
            ViewModel.SelectedSection = section;
        }
    }
}
