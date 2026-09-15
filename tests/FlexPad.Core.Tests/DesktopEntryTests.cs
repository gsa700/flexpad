using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class DesktopEntryTests
{
    private const string Exec = "/home/ab0r/.local/share/flexpad/FlexPad";

    [Fact]
    public void HasTheRequiredKeys()
    {
        var s = DesktopEntry.Build("FlexPad", Exec);
        Assert.StartsWith("[Desktop Entry]", s);
        Assert.Contains("Type=Application", s);
        Assert.Contains("Name=FlexPad", s);
        Assert.Contains("Terminal=false", s);
    }

    [Fact]
    public void ExecIsQuoted()
    {
        var s = DesktopEntry.Build("FlexPad", Exec);
        Assert.Contains($"Exec=\"{Exec}\"", s);
    }

    [Fact]
    public void APathWithSpacesSurvivesQuoting()
    {
        // The Windows install folder has a space in it; if the Linux one ever does too, the entry
        // must still launch rather than trying to run a truncated path.
        var p = "/home/ab0r/.local/share/FlexPad/FlexPad";
        Assert.Equal($"\"{p}\"", DesktopEntry.QuoteExec(p));
    }

    [Theory]
    [InlineData("/opt/a\"b", "\"/opt/a\\\"b\"")]
    [InlineData("/opt/a$b", "\"/opt/a\\$b\"")]
    [InlineData("/opt/a`b", "\"/opt/a\\`b\"")]
    [InlineData("/opt/a\\b", "\"/opt/a\\\\b\"")]
    public void ReservedCharactersAreEscapedInsideTheQuotes(string path, string expected)
    {
        Assert.Equal(expected, DesktopEntry.QuoteExec(path));
    }

    [Fact]
    public void ALiteralPercentIsDoubledSoItIsNotReadAsAFieldCode()
    {
        // %f, %U and friends are expanded by the launcher before anything runs.
        Assert.Equal("\"/opt/100%%/app\"", DesktopEntry.QuoteExec("/opt/100%/app"));
    }

    [Fact]
    public void TheIconLineIsOmittedRatherThanLeftEmpty()
    {
        // Better no Icon key than one pointing at a file that isn't there.
        var s = DesktopEntry.Build("FlexPad", Exec, iconPath: null);
        Assert.DoesNotContain("Icon=", s);
    }

    [Fact]
    public void TheIconIsIncludedWhenGiven()
    {
        var icon = "/home/ab0r/.local/share/icons/hicolor/256x256/apps/flexpad.png";
        Assert.Contains($"Icon={icon}", DesktopEntry.Build("FlexPad", Exec, iconPath: icon));
    }

    [Fact]
    public void CategoriesDefaultToAMainCategoryAlongsideHamRadio()
    {
        // HamRadio is an additional category; on its own, conforming menus may file it nowhere.
        var s = DesktopEntry.Build("FlexPad", Exec);
        Assert.Contains("Categories=Utility;HamRadio;", s);
    }

    [Fact]
    public void CategoriesEndWithASemicolonBecauseTheKeyIsAList()
    {
        var s = DesktopEntry.Build("FlexPad", Exec, categories: ["Network"]);
        Assert.Contains("Categories=Network;\n", s);
    }

    [Fact]
    public void TheWindowClassMatchesTheAssemblyNameSoTheDockMatchesTheWindow()
    {
        // Avalonia reports the assembly name as the X11 class; "FlexPad" is what FlexPad.App produces.
        Assert.Equal("FlexPad", DesktopEntry.WindowClass);
        Assert.Contains($"StartupWMClass={DesktopEntry.WindowClass}", DesktopEntry.Build("x", Exec));
    }

    [Fact]
    public void NewlinesInValuesCannotForgeExtraKeys()
    {
        // A newline in a value would start a bogus key and corrupt everything after it. The text
        // may still appear inside the flattened value — what matters is that it is never a line of
        // its own, because only a line is a key.
        var s = DesktopEntry.Build("Evil\nExec=/bin/sh", Exec);
        var lines = s.Split('\n');
        Assert.DoesNotContain(lines, l => l.StartsWith("Exec=/bin/sh"));
        Assert.Single(lines, l => l.StartsWith("Exec="));
        Assert.Contains("Name=Evil Exec=/bin/sh", s);
    }

    [Fact]
    public void EveryLineIsAKeyValuePairOrTheGroupHeader()
    {
        var s = DesktopEntry.Build("FlexPad", Exec, iconPath: "/tmp/i.png", comment: "Wattmeter");
        foreach (var line in s.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(line == "[Desktop Entry]" || line.Contains('='), $"stray line: {line}");
        }
    }
}
