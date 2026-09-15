using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class XdgUserDirsTests
{
    private const string Home = "/home/ab0r";

    private static string? Desktop(string? contents) =>
        XdgUserDirs.Resolve(contents, XdgUserDirs.DesktopKey, Home);

    [Fact]
    public void ReadsTheOrdinaryFormRaspberryPiOsWrites() =>
        Assert.Equal("/home/ab0r/Desktop", Desktop("XDG_DESKTOP_DIR=\"$HOME/Desktop\"\n"));

    [Fact]
    public void ReadsALocalisedDirectory() =>
        // The whole reason this isn't hardcoded to ~/Desktop.
        Assert.Equal("/home/ab0r/Escritorio", Desktop("XDG_DESKTOP_DIR=\"$HOME/Escritorio\"\n"));

    [Fact]
    public void ReadsAnAbsolutePathOutsideHome() =>
        Assert.Equal("/mnt/shared/desk", Desktop("XDG_DESKTOP_DIR=\"/mnt/shared/desk\"\n"));

    [Fact]
    public void PicksTheRightKeyOutOfAFullFile()
    {
        var file = """
            # This file is written by xdg-user-dirs-update
            XDG_DOWNLOAD_DIR="$HOME/Downloads"
            XDG_DESKTOP_DIR="$HOME/Desktop"
            XDG_DOCUMENTS_DIR="$HOME/Documents"
            """;
        Assert.Equal("/home/ab0r/Desktop", Desktop(file));
        Assert.Equal("/home/ab0r/Downloads", XdgUserDirs.Resolve(file, "XDG_DOWNLOAD_DIR", Home));
    }

    [Fact]
    public void IgnoresACommentedOutKey() =>
        Assert.Null(Desktop("#XDG_DESKTOP_DIR=\"$HOME/Desktop\"\n"));

    [Fact]
    public void DoesNotMatchAKeyThatMerelyEndsWithTheName() =>
        Assert.Null(Desktop("MY_XDG_DESKTOP_DIR=\"$HOME/Nope\"\n"));

    [Theory]
    [InlineData("XDG_DESKTOP_DIR=\"$HOME/\"")]   // the convention for "this user has no desktop"
    [InlineData("XDG_DESKTOP_DIR=\"$HOME\"")]
    [InlineData("XDG_DESKTOP_DIR=\"\"")]
    [InlineData("XDG_DESKTOP_DIR=")]
    public void NoDesktopMeansNull(string line) =>
        // Writing a launcher into $HOME on such a machine drops a file at the top of someone's home
        // directory, which is worse than not creating a shortcut at all.
        Assert.Null(Desktop(line));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nothing relevant here\n")]
    public void MissingOrIrrelevantContentIsNull(string? contents) =>
        Assert.Null(Desktop(contents));

    [Fact]
    public void TolerantOfWhitespaceAndCrlf() =>
        Assert.Equal("/home/ab0r/Desktop", Desktop("  XDG_DESKTOP_DIR = \"$HOME/Desktop\" \r\n"));

    [Fact]
    public void TrailingSlashIsTrimmedSoPathsCompareEqual() =>
        Assert.Equal("/home/ab0r/Desktop", Desktop("XDG_DESKTOP_DIR=\"$HOME/Desktop/\"\n"));

    [Fact]
    public void LastAssignmentWins() =>
        // Matches how the shell reads the file, and these get appended to by more than one tool.
        Assert.Equal("/home/ab0r/Second",
            Desktop("XDG_DESKTOP_DIR=\"$HOME/First\"\nXDG_DESKTOP_DIR=\"$HOME/Second\"\n"));
}
