using Microsoft.UI.Xaml.Controls;
using NeoContext.App.ViewModels;

namespace NeoContext.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>Set once by the main window before the view is shown.</summary>
    public SettingsViewModel? ViewModel
    {
        get;
        set
        {
            field = value;
            Bindings.Update();
        }
    }
}
