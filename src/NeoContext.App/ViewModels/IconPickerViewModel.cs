using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using NeoContext.App.Services;

namespace NeoContext.App.ViewModels;

/// <summary>One icon in the picker grid. The image loads after the grid is shown.</summary>
public sealed partial class IconPickItem(int index) : ObservableObject
{
    public int Index { get; } = index;

    [ObservableProperty]
    public partial ImageSource? Image { get; set; }
}

/// <summary>Shows every icon inside an .exe, .dll, .ico or .icl so one can be picked.</summary>
public sealed partial class IconPickerViewModel : ObservableObject
{
    private int _version;
    private string _resolvedFile = "";

    public ObservableCollection<IconPickItem> Items { get; } = [];

    [ObservableProperty]
    public partial string FilePath { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public async Task LoadAsync(string file)
    {
        var version = ++_version;
        FilePath = file;
        Items.Clear();
        Status = "";

        var resolved = Environment.ExpandEnvironmentVariables(file.Trim().Trim('"'));
        if (resolved.Length > 0 && !Path.IsPathRooted(resolved))
        {
            resolved = Path.Combine(Environment.SystemDirectory, resolved);
        }

        if (!File.Exists(resolved))
        {
            Status = Strings.Get("IconPicker_NotFound");
            return;
        }

        IsLoading = true;
        try
        {
            var count = await Task.Run(() => IconExtractor.Count(resolved));
            if (version != _version)
            {
                return;
            }

            if (count == 0)
            {
                Status = Strings.Get("IconPicker_Empty");
                return;
            }

            _resolvedFile = resolved;
            var items = Enumerable.Range(0, count).Select(i => new IconPickItem(i)).ToList();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            // Icons arrive one by one, so a big file such as imageres.dll does not freeze the window.
            foreach (var item in items)
            {
                var pixels = await Task.Run(() => IconExtractor.Extract(resolved, item.Index));
                if (version != _version)
                {
                    return;
                }

                if (pixels is not null)
                {
                    item.Image = await IconImage.FromPixelsAsync(pixels);
                }
            }
        }
        finally
        {
            if (version == _version)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>The <c>file,index</c> text for the registry. Files in System32 are written with %SystemRoot%.</summary>
    public string Compose(IconPickItem item)
    {
        var system = Environment.SystemDirectory;
        var file = string.Equals(Path.GetDirectoryName(_resolvedFile), system, StringComparison.OrdinalIgnoreCase)
            ? $@"%SystemRoot%\System32\{Path.GetFileName(_resolvedFile)}"
            : _resolvedFile;
        return $"{file},{item.Index}";
    }
}
