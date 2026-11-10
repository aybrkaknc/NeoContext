using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using NeoContext.App.Services;
using NeoContext.App.ViewModels;
using NeoContext.Updates;

namespace NeoContext.App.Views;

/// <summary>The only window. It switches between the entry pages and the settings page.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow(AppHost host)
    {
        var dialogs = new DialogService(() => Content.XamlRoot);
        ViewModel = new MainViewModel(host, dialogs);
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

        SettingsPanel.ViewModel = new SettingsViewModel(host, dialogs);
        BuildNavigation();
        _ = ViewModel.RefreshAsync();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>Fills the left menu: each group is expandable; a group with one page is shown as that page.</summary>
    private void BuildNavigation()
    {
        NavigationViewItem? first = null;
        foreach (var group in ViewModel.SectionGroups)
        {
            var items = group.Items.Select(CreateItem).ToList();
            first ??= items[0];

            if (items.Count == 1)
            {
                Navigation.MenuItems.Add(items[0]);
                continue;
            }

            var parent = new NavigationViewItem
            {
                Content = group.Title,
                Icon = new FontIcon { Glyph = group.Glyph },
                SelectsOnInvoked = false,
                IsExpanded = true,
            };
            foreach (var item in items)
            {
                parent.MenuItems.Add(item);
            }

            Navigation.MenuItems.Add(parent);
        }

        Navigation.SelectedItem = first;
    }

    private static NavigationViewItem CreateItem(SectionItem section)
    {
        var item = new NavigationViewItem
        {
            Content = section.Title,
            Tag = section,
            Icon = new FontIcon { Glyph = section.Glyph },
        };
        ToolTipService.SetToolTip(item, section.Description);
        return item;
    }

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

    public Visibility ToVisible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void OnOverlayTapped(object sender, TappedRoutedEventArgs e) => ViewModel.CloseDetailsCommand.Execute(null);

    /// <summary>Taps on the card stay on the card, so only taps outside it close the overlay.</summary>
    private void OnCardTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void OnRemoveChild(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NewChildViewModel child } && ViewModel.Creating is { } creating)
        {
            creating.Children.Remove(child);
        }
    }

    private async void OnBrowseProgram(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            foreach (var extension in new[] { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".com" })
            {
                picker.FileTypeFilter.Add(extension);
            }

            if (await picker.PickSingleFileAsync() is { } file && ViewModel.Creating is { } creating)
            {
                creating.Program = file.Path;
                if (string.IsNullOrWhiteSpace(creating.Name))
                {
                    creating.Name = Path.GetFileNameWithoutExtension(file.Path);
                }
            }
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Warn($"File picker failed: {ex.Message}");
        }
    }

    private void OnIconPickerBackdropTapped(object sender, TappedRoutedEventArgs e) => ViewModel.CloseIconPickerCommand.Execute(null);

    private void OnIconClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is IconPickItem item)
        {
            ViewModel.ChooseIcon(item);
        }
    }

    private void OnLoadIconFile(object sender, RoutedEventArgs e) => _ = ViewModel.LoadIconFileAsync(IconFileBox.Text);

    private void OnQuickIconFile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string file })
        {
            _ = ViewModel.LoadIconFileAsync(file);
        }
    }

    private async void OnBrowseIconFile(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            foreach (var extension in new[] { ".exe", ".dll", ".ico", ".icl", ".ocx" })
            {
                picker.FileTypeFilter.Add(extension);
            }

            if (await picker.PickSingleFileAsync() is { } file)
            {
                await ViewModel.LoadIconFileAsync(file.Path);
            }
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Warn($"File picker failed: {ex.Message}");
        }
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsIconPickerOpen)
        {
            ViewModel.CloseIconPickerCommand.Execute(null);
            args.Handled = true;
            return;
        }

        if (ViewModel.IsEditOpen)
        {
            ViewModel.CancelEditCommand.Execute(null);
            args.Handled = true;
            return;
        }

        if (ViewModel.IsNewOpen)
        {
            ViewModel.CancelNewCommand.Execute(null);
            args.Handled = true;
            return;
        }

        if (ViewModel.IsDetailsOpen)
        {
            ViewModel.CloseDetailsCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnFileTypeGroupClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FileTypeChoice choice })
        {
            ViewModel.SelectFileTypeGroupCommand.Execute(choice);
        }
    }

    private void OnExtensionTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = ViewModel.SuggestExtensions(sender.Text);
        }
    }

    private void OnExtensionSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = ViewModel.SubmitExtensionsAsync(args.ChosenSuggestion as string ?? args.QueryText);

    private void OnEntryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EntryViewModel entry)
        {
            entry.ShowDetailsCommand.Execute(null);
        }
    }

    private void OnFocusSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void OnClearSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.SearchText = "";
        args.Handled = true;
    }

    private void OnRefreshShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.RefreshCommand.Execute(null);
        args.Handled = true;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var showSettings = args.IsSettingsSelected;
        SettingsPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        EntriesPanel.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;

        if (!showSettings && args.SelectedItem is NavigationViewItem { Tag: SectionItem section })
        {
            ViewModel.SearchText = "";
            ViewModel.SelectedSection = section;
        }
    }
}
