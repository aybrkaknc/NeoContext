using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NeoContext.Core.Rules;

namespace NeoContext.App.Services;

/// <summary>Asks the user something. View models use this interface and never touch controls.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false);

    /// <summary>Asks for a name. Returns null when the user cancels.</summary>
    Task<string?> PromptForNameAsync(string title, string initialValue);
}

/// <summary>Shows ContentDialogs on the main window.</summary>
public sealed class DialogService(Func<XamlRoot?> xamlRoot) : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false)
    {
        var dialog = Create(title);
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        dialog.PrimaryButtonText = confirmText;
        dialog.CloseButtonText = Strings.Get("Dialog_Cancel");
        dialog.DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<string?> PromptForNameAsync(string title, string initialValue)
    {
        var box = new TextBox { Text = initialValue, MaxLength = EntryNameRules.MaxLength, SelectionStart = 0, SelectionLength = initialValue.Length };
        var hint = new TextBlock
        {
            Text = Strings.Format("Rename_Hint", EntryNameRules.MaxLength),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Margin = new Thickness(0, 8, 0, 0),
        };

        var dialog = Create(title);
        dialog.Content = new StackPanel { Children = { box, hint } };
        dialog.PrimaryButtonText = Strings.Get("Dialog_Save");
        dialog.CloseButtonText = Strings.Get("Dialog_Cancel");
        dialog.DefaultButton = ContentDialogButton.Primary;
        box.TextChanged += (_, _) =>
        {
            var valid = EntryNameRules.Validate(box.Text).IsSuccess;
            dialog.IsPrimaryButtonEnabled = valid;
            hint.Text = Strings.Format(valid ? "Rename_Hint" : "Rename_Invalid", EntryNameRules.MaxLength);
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    private ContentDialog Create(string title) => new()
    {
        Title = title,
        XamlRoot = xamlRoot(),
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
    };
}
