using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeoContext.App.Services;
using NeoContext.Core.Models;
using NeoContext.Core.Rules;

namespace NeoContext.App.ViewModels;

/// <summary>One child row in the new-submenu list.</summary>
public sealed partial class NewChildViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Command { get; set; } = "";
}

/// <summary>The form behind "New item": a verb (single command or submenu) or a shell extension by GUID.</summary>
public sealed partial class NewItemViewModel : ObservableObject
{
    public NewItemViewModel(MenuSection section)
    {
        Section = section;
        SelectionToken = ShellCommands.SelectionToken(section);
        PassSelected = SelectionToken.Length > 0;
        Children.Add(new NewChildViewModel());
    }

    public MenuSection Section { get; }

    /// <summary>True on pages that only take a shell extension by GUID (Blocked GUIDs): no kind choice, per-user only.</summary>
    public bool HandlerOnly { get; init; }

    public bool ShowTypeChoice => !HandlerOnly;

    public bool ShowAllUsers => !HandlerOnly;

    /// <summary>What Explorer fills in for the clicked item on this page; empty when nothing is clicked.</summary>
    public string SelectionToken { get; }

    public string Title => Strings.Format("New_Title", Strings.Get($"Section_{Section}"));

    public ObservableCollection<NewChildViewModel> Children { get; } = [];

    /// <summary>0 = shell verb, 1 = shell extension (GUID).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVerb), nameof(IsHandler), nameof(ShowSingle), nameof(ShowSubmenu), nameof(ShowSelectionOption))]
    public partial int TypeIndex { get; set; }

    /// <summary>0 = single command, 1 = submenu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSingle), nameof(ShowSubmenu), nameof(ShowSelectionOption))]
    public partial int ModeIndex { get; set; }

    public bool IsVerb => TypeIndex == 0;

    public bool IsHandler => TypeIndex == 1;

    public bool ShowSingle => IsVerb && ModeIndex == 0;

    public bool ShowSubmenu => IsVerb && ModeIndex == 1;

    public bool ShowSelectionOption => ShowSingle && SelectionToken.Length > 0;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Program { get; set; } = "";

    [ObservableProperty]
    public partial string Arguments { get; set; } = "";

    [ObservableProperty]
    public partial bool PassSelected { get; set; }

    [ObservableProperty]
    public partial bool Hidden { get; set; }

    [ObservableProperty]
    public partial bool RunAsAdministrator { get; set; }

    [ObservableProperty]
    public partial bool AllUsers { get; set; }

    [ObservableProperty]
    public partial string Icon { get; set; } = "";

    [ObservableProperty]
    public partial string GuidText { get; set; } = "";

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    [RelayCommand]
    private void AddChild() => Children.Add(new NewChildViewModel());

    /// <summary>The verb to create, or null (with <see cref="Error"/> set) when the form is not valid.</summary>
    public NewVerb? ToVerb()
    {
        if (!EntryNameRules.Validate(Name).IsSuccess)
        {
            Error = Strings.Format("Rename_Invalid", EntryNameRules.MaxLength);
            return null;
        }

        var icon = string.IsNullOrWhiteSpace(Icon) ? null : Icon.Trim();
        var attributes = new VerbAttributes { Shield = ShowSingle && RunAsAdministrator };

        if (ShowSubmenu)
        {
            var children = Children
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.Command))
                .ToList();
            if (children.Count == 0 || children.Any(c => !EntryNameRules.Validate(c.Name).IsSuccess || string.IsNullOrWhiteSpace(c.Command)))
            {
                Error = Strings.Get("New_ChildrenInvalid");
                return null;
            }

            return new NewVerb(Name.Trim(), null, null, icon, attributes,
                [.. children.Select(c => new NewVerbChild(c.Name.Trim(), c.Command.Trim()))]);
        }

        if (string.IsNullOrWhiteSpace(Program))
        {
            Error = Strings.Get("New_ProgramRequired");
            return null;
        }

        var token = PassSelected ? SelectionToken : "";
        return Hidden || RunAsAdministrator
            ? new NewVerb(Name.Trim(), null, new NewVerbScript(Program.Trim(), Arguments, token, Hidden, RunAsAdministrator), icon, attributes, [])
            : new NewVerb(Name.Trim(), ShellCommands.Direct(Program, Arguments, token), null, icon, attributes, []);
    }

    /// <summary>The CLSID to add, or null (with <see cref="Error"/> set) when the text is not a GUID.</summary>
    public Guid? ToGuid()
    {
        if (Guid.TryParse(GuidText.Trim(), out var clsid))
        {
            return clsid;
        }

        Error = Strings.Get("New_GuidInvalid");
        return null;
    }
}
