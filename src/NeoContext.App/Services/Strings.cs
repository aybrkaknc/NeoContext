using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace NeoContext.App.Services;

/// <summary>
/// Localized strings from <c>Strings/&lt;language&gt;/Resources.resw</c>. XAML uses <c>x:Uid</c>;
/// code uses this class. A missing key returns the key itself, so gaps are visible, not blank.
/// </summary>
public static class Strings
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key)
    {
        try
        {
            var value = Loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return key;
        }
    }

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
