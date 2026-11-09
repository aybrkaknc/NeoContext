using Microsoft.Win32;
using NeoContext.Registry;

namespace NeoContext.Tests.Registry;

public class RegistryPathTests
{
    [Theory]
    [InlineData(@"HKCR\*\shell\open", RegistryHive.ClassesRoot, @"*\shell\open")]
    [InlineData(@"HKEY_CLASSES_ROOT\*\shell\open", RegistryHive.ClassesRoot, @"*\shell\open")]
    [InlineData(@"hkcu\Software\", RegistryHive.CurrentUser, "Software")]
    [InlineData("HKLM", RegistryHive.LocalMachine, "")]
    public void Parses_long_and_short_hive_names(string text, RegistryHive hive, string subKey)
    {
        var path = RegistryPath.Parse(text);

        Assert.Equal(hive, path.Hive);
        Assert.Equal(subKey, path.SubKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("shell\\open")]
    [InlineData("HKXX\\foo")]
    public void Rejects_unknown_hives(string text) => Assert.False(RegistryPath.TryParse(text, out _));

    [Fact]
    public void Name_parent_and_combine()
    {
        var path = RegistryPath.Parse(@"HKCR\Directory\shell") / "cmd";

        Assert.Equal("cmd", path.Name);
        Assert.Equal(@"HKEY_CLASSES_ROOT\Directory\shell", path.Parent.ToString());
        Assert.Equal(@"HKEY_CLASSES_ROOT\Directory\shell\-cmd", path.WithName("-cmd").ToString());
    }

    [Fact]
    public void Round_trips_through_ToString()
    {
        var path = RegistryPath.Parse(@"HKCU\Software\Classes\*\shellex");

        Assert.Equal(path, RegistryPath.Parse(path.ToString()));
    }
}
