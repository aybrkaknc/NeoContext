using CommunityToolkit.Mvvm.ComponentModel;
using NeoContext.App.Services;
using NeoContext.Core.Models;

namespace NeoContext.App.ViewModels;

/// <summary>A Win+X group in the group chooser: the folder name and the label shown for it.</summary>
public sealed record GroupOption(string Name, string Label);

/// <summary>
/// The form for pages whose entries are shortcuts to something (Send to, Win+X) or a registered
/// program (Open with): a name, a program, file or folder, and optional arguments.
/// </summary>
public sealed partial class NewTargetViewModel(MenuSection section, IReadOnlyList<GroupOption>? groups = null) : ObservableObject
{
    public MenuSection Section { get; } = section;

    /// <summary>The Win+X groups to choose from; empty on the other pages.</summary>
    public IReadOnlyList<GroupOption> Groups { get; } = groups ?? [];

    public bool ShowGroup => Section == MenuSection.WinX;

    [ObservableProperty]
    public partial int GroupIndex { get; set; }

    /// <summary>The folder name of the chosen group, or null when there is none.</summary>
    public string? GroupName => GroupIndex >= 0 && GroupIndex < Groups.Count ? Groups[GroupIndex].Name : null;

    public string Title => Strings.Format("NewTarget_Title", Strings.Get($"Section_{Section}"));

    /// <summary>A Send to shortcut can point to a folder too, so that page offers a folder picker.</summary>
    public bool ShowFolderButton => Section == MenuSection.SendTo;

    /// <summary>Open with registers a program by its path only, so it has no arguments.</summary>
    public bool ShowArguments => Section != MenuSection.OpenWith;

    /// <summary>Open with is a registry entry, per user by default; Send to is a file in the user's own folder.</summary>
    public bool ShowAllUsers => Section == MenuSection.OpenWith;

    [ObservableProperty]
    public partial bool AllUsers { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Target { get; set; } = "";

    [ObservableProperty]
    public partial string Arguments { get; set; } = "";

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    public bool Validate()
    {
        if (ShowGroup && GroupName is null)
        {
            Error = Strings.Get("NewTarget_NoGroups");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Target))
        {
            Error = Strings.Get("NewTarget_TargetRequired");
            return false;
        }

        Error = "";
        return true;
    }
}
