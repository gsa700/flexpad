using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class InstallLayoutTests
{
    // Paths are built with Path.Combine so the separators are native wherever the tests run, and
    // the naming convention is passed explicitly so these don't change meaning on a Linux runner.
    private static string Programs => Path.Combine("C:", "Users", "ab0r", "AppData", "Local", "Programs");
    private static string InstallDir => InstallLayout.InstallDirectoryUnder(Programs, windows: true);
    private static IEnumerable<string> Installed =>
        InstallLayout.InstalledDirectoriesUnder(Programs, windows: true);

    [Fact]
    public void RunningFromTheInstallDirectoryIsInstalled()
    {
        var mode = InstallLayout.Detect(InstallDir, portableMarkerPresent: false, InstallDir);
        Assert.Equal(InstallMode.Installed, mode);
    }

    [Fact]
    public void AFreshlyUnzippedDownloadIsLoose()
    {
        var downloads = Path.Combine("C:", "Users", "ab0r", "Downloads", "FlexPad-win-x64");
        var mode = InstallLayout.Detect(downloads, portableMarkerPresent: false, InstallDir, Installed);
        Assert.Equal(InstallMode.Loose, mode);
    }

    [Fact]
    public void TheMarkerMakesACopyPortable()
    {
        var stick = Path.Combine("E:", "W2");
        var mode = InstallLayout.Detect(stick, portableMarkerPresent: true, InstallDir, Installed);
        Assert.Equal(InstallMode.Portable, mode);
    }

    [Fact]
    public void TheMarkerWinsEvenInsideTheInstallDirectory()
    {
        // One unambiguous way to say "touch nothing", which can't be defeated by where the copy sits.
        var mode = InstallLayout.Detect(InstallDir, portableMarkerPresent: true, InstallDir, Installed);
        Assert.Equal(InstallMode.Portable, mode);
    }

    [Fact]
    public void TheHandUnzippedStationInstallIsAdoptedWhereItStands()
    {
        // Not hypothetical: the Windows box this was written on runs from exactly this folder, put
        // there by unzipping a release in Explorer. Adopting it is what stops the first run of the
        // installer from creating a second copy and orphaning the one actually in use.
        var legacy = Path.Combine(Programs, "FlexPad-win-x64");
        var mode = InstallLayout.Detect(legacy, portableMarkerPresent: false, InstallDir, Installed);
        Assert.Equal(InstallMode.Installed, mode);
    }

    [Theory]
    [InlineData("FlexPad")]
    [InlineData("FlexPad-win-x64")]
    [InlineData("FlexPad-linux-x64")]
    [InlineData("FlexPad-linux-arm64")]
    public void EveryReleaseZipsFolderNameIsAdopted(string folder)
    {
        // One per published RID, because the folder Explorer creates is named after the zip.
        var mode = InstallLayout.Detect(
            Path.Combine(Programs, folder), portableMarkerPresent: false, InstallDir, Installed);
        Assert.Equal(InstallMode.Installed, mode);
    }

    [Fact]
    public void ATrailingSeparatorDoesNotChangeTheAnswer()
    {
        var withSlash = InstallDir + Path.DirectorySeparatorChar;
        var mode = InstallLayout.Detect(withSlash, portableMarkerPresent: false, InstallDir, Installed);
        Assert.Equal(InstallMode.Installed, mode);
    }

    [Fact]
    public void WindowsPathsCompareCaseInsensitively()
    {
        Assert.True(InstallLayout.SamePath(
            Path.Combine("C:", "Program Files", "App"),
            Path.Combine("c:", "program files", "app"),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CaseSensitiveComparisonSeparatesDirectoriesThatDifferOnlyByCase()
    {
        // On Linux ~/Apps and ~/apps are genuinely different directories.
        Assert.False(InstallLayout.SamePath(
            Path.Combine("home", "ab0r", "Apps"),
            Path.Combine("home", "ab0r", "apps"),
            StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyLegacyListIsFine()
    {
        var elsewhere = Path.Combine("D:", "tools", "w2");
        var mode = InstallLayout.Detect(elsewhere, portableMarkerPresent: false, InstallDir, alsoInstalled: null);
        Assert.Equal(InstallMode.Loose, mode);
    }

    [Fact]
    public void TheInstallDirectorySitsUnderTheProgramsDirectory()
    {
        Assert.Equal(Path.Combine(Programs, "FlexPad"),
            InstallLayout.InstallDirectoryUnder(Programs, windows: true));
    }

    [Fact]
    public void LinuxUsesTheXdgStyleFolderName()
    {
        // Lower-case, hyphenated, and free of the space that would need quoting in the .desktop
        // entry's Exec line.
        var share = Path.Combine("home", "ab0r", ".local", "share");
        Assert.Equal(Path.Combine(share, "flexpad"),
            InstallLayout.InstallDirectoryUnder(share, windows: false));
    }

    [Fact]
    public void TheUnixFolderNameHasNoSpaces()
    {
        Assert.DoesNotContain(' ', InstallLayout.ProductFolderUnix);
    }

    [Fact]
    public void TheCanonicalDirectoryIsOfferedBeforeTheLegacyOnes()
    {
        // Order matters: an install should land in the canonical folder, not the first legacy match.
        Assert.Equal(InstallDir, Installed.First());
    }
}

public class InstallCommandLineTests
{
    [Fact]
    public void NoArgumentsMeansJustRunTheApp()
    {
        var r = InstallCommandLine.Parse([]);
        Assert.Equal(InstallAction.None, r.Action);
        Assert.False(r.Quiet);
    }

    [Fact]
    public void NullArgumentsAreTreatedAsNone()
    {
        Assert.Equal(InstallAction.None, InstallCommandLine.Parse(null).Action);
    }

    [Fact]
    public void InstallIsRecognised()
    {
        Assert.Equal(InstallAction.Install, InstallCommandLine.Parse(["--install"]).Action);
    }

    [Fact]
    public void UninstallIsRecognised()
    {
        Assert.Equal(InstallAction.Uninstall, InstallCommandLine.Parse(["--uninstall"]).Action);
    }

    [Theory]
    [InlineData("--quiet")]
    [InlineData("-quiet")]
    [InlineData("/quiet")]
    [InlineData("--QUIET")]
    public void SwitchPrefixesAndCasingAreAllAccepted(string arg)
    {
        // Shortcuts, the installed-apps entry and a hand-typed command each have their own habits.
        var r = InstallCommandLine.Parse(["--install", arg]);
        Assert.True(r.Quiet);
    }

    [Fact]
    public void UnknownArgumentsAreIgnored()
    {
        // Avalonia and the OS pass switches of their own; none may be mistaken for an instruction
        // to modify the machine. --sim and --setup are this app's own, and must survive untouched.
        var r = InstallCommandLine.Parse(["--sim", "--setup", "/renderer", "gpu"]);
        Assert.Equal(InstallAction.None, r.Action);
        Assert.False(r.Quiet);
    }

    [Fact]
    public void UninstallBeatsInstallWhenBothArePassed()
    {
        // Between contradictory instructions, prefer the one that does less to the machine.
        var r = InstallCommandLine.Parse(["--install", "--uninstall"]);
        Assert.Equal(InstallAction.Uninstall, r.Action);
    }

    [Fact]
    public void QuietIsCarriedThroughOnUninstall()
    {
        // What the installed-apps entry passes: Windows gives no way to answer a dialog it didn't
        // expect, so that path must not prompt.
        var r = InstallCommandLine.Parse(["--uninstall", "--quiet"]);
        Assert.Equal(InstallAction.Uninstall, r.Action);
        Assert.True(r.Quiet);
    }
}
