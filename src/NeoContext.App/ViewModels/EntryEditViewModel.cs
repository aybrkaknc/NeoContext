using CommunityToolkit.Mvvm.ComponentModel;
using NeoContext.App.Services;
using NeoContext.Core.Models;

namespace NeoContext.App.ViewModels;

/// <summary>The form behind "Edit": command, icon, position and the verb's flags.</summary>
public sealed partial class EntryEditViewModel : ObservableObject
{
    public EntryEditViewModel(ContextMenuEntry entry)
    {
        Entry = entry;
        Command = entry.Command ?? "";
        ValueText = entry.Value?.Current ?? "";
        Icon = entry.IconLocation ?? "";

        var attributes = entry.Attributes ?? new VerbAttributes();
        PositionIndex = (int)attributes.Position;
        Shield = attributes.Shield;
        Extended = attributes.Extended;
        OnlyInBrowserWindow = attributes.OnlyInBrowserWindow;
        NoWorkingDirectory = attributes.NoWorkingDirectory;
        NeverDefault = attributes.NeverDefault;
        ShowAsDisabledIfHidden = attributes.ShowAsDisabledIfHidden;
    }

    public ContextMenuEntry Entry { get; }

    public string Title => Entry.DisplayName;

    /// <summary>Submenu headers have no command of their own, so the box is hidden for them.</summary>
    public bool HasCommand => Entry.Command is not null;

    /// <summary>True for settings that hold a number or text: only the value is edited.</summary>
    public bool IsValue => Entry.Value is not null;

    public bool ShowVerbFields => !IsValue;

    public string ValueHint => Entry.Value is not { } spec
        ? ""
        : spec.IsNumber
            ? Strings.Format("Edit_ValueHint", spec.Min ?? 0, spec.Max ?? int.MaxValue, spec.Default ?? 0)
            : Strings.Get("Edit_ValueHintText");

    [ObservableProperty]
    public partial string ValueText { get; set; } = "";

    [ObservableProperty]
    public partial string Command { get; set; } = "";

    [ObservableProperty]
    public partial string Icon { get; set; } = "";

    /// <summary>0 = default, 1 = top, 2 = bottom (same order as <see cref="VerbPosition"/>).</summary>
    [ObservableProperty]
    public partial int PositionIndex { get; set; }

    [ObservableProperty]
    public partial bool Shield { get; set; }

    [ObservableProperty]
    public partial bool Extended { get; set; }

    [ObservableProperty]
    public partial bool OnlyInBrowserWindow { get; set; }

    [ObservableProperty]
    public partial bool NoWorkingDirectory { get; set; }

    [ObservableProperty]
    public partial bool NeverDefault { get; set; }

    [ObservableProperty]
    public partial bool ShowAsDisabledIfHidden { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    /// <summary>The change to apply, or null (with <see cref="Error"/> set) when the form is not valid.</summary>
    public EntryEdit? ToEdit()
    {
        if (IsValue)
        {
            if (Entry.Value is { IsNumber: true } spec && ValueText.Trim().Length > 0
                && !(int.TryParse(ValueText.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
                     && number >= (spec.Min ?? int.MinValue) && number <= (spec.Max ?? int.MaxValue)))
            {
                Error = ValueHint;
                return null;
            }

            return new EntryEdit(null, null, new VerbAttributes()) { Value = ValueText.Trim() };
        }

        if (HasCommand && string.IsNullOrWhiteSpace(Command))
        {
            Error = Strings.Get("Edit_CommandRequired");
            return null;
        }

        var command = Command.Trim();
        var icon = Icon.Trim();
        return new EntryEdit(
            HasCommand && command != Entry.Command ? command : null,
            icon != (Entry.IconLocation ?? "") ? icon : null,
            new VerbAttributes
            {
                Position = (VerbPosition)PositionIndex,
                Shield = Shield,
                Extended = Extended,
                OnlyInBrowserWindow = OnlyInBrowserWindow,
                NoWorkingDirectory = NoWorkingDirectory,
                NeverDefault = NeverDefault,
                ShowAsDisabledIfHidden = ShowAsDisabledIfHidden,
            });
    }
}
