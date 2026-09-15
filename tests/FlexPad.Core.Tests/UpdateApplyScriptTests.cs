using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class UpdateApplyScriptTests
{
    const int Pid = 4242;
    const string Staged = "/tmp/FlexPad-update/ex/FlexPad";
    const string Target = "/opt/flexpad/FlexPad";
    const string Marker = "/opt/flexpad/.flexpad-update-failed";
    const string WorkDir = "/opt/flexpad";
    const string StageRoot = "/tmp/FlexPad-update";
    const string ScriptPath = "/tmp/flexpad-apply-update.sh";

    static string Win() => UpdateApplyScript.Windows(Pid, Staged, Target, Marker, WorkDir, StageRoot, ScriptPath);
    static string Nix() => UpdateApplyScript.Unix(Pid, Staged, Target, Marker, WorkDir, StageRoot, ScriptPath);

    [Fact]
    public void Windows_waits_for_the_process_then_copies_and_relaunches()
    {
        var s = Win();
        Assert.Contains($"-Id {Pid}", s);
        Assert.Contains(Staged, s);
        Assert.Contains(Target, s);
        Assert.Contains("Start-Process", s);
    }

    [Fact]
    public void Windows_gates_the_relaunch_impression_on_the_copy_result()
    {
        var s = Win();
        // Copy-Item is non-terminating, so the script must check $? and record failure via the marker.
        Assert.Contains("if ($?)", s);
        Assert.Contains(Marker, s);
        // Success path clears any stale marker; failure path creates one.
        Assert.Contains("Remove-Item", s);
        Assert.Contains("New-Item", s);
    }

    [Fact]
    public void Unix_only_relaunches_the_new_build_when_cp_succeeds()
    {
        var s = Nix();
        Assert.Contains($"kill -0 {Pid}", s);
        // Relaunch of the swapped exe is conditional on cp succeeding.
        Assert.Contains($"if cp -f '{Staged}' '{Target}'; then", s);
        Assert.Contains("chmod +x", s);
        Assert.Contains(Marker, s);   // failure branch records the marker
        Assert.Contains($"'{Target}' &", s);
    }

    [Fact]
    public void Windows_starts_the_app_in_the_install_directory_not_the_staging_one()
    {
        // Without this the app inherits the helper's directory. A directory held as a process's
        // working directory cannot be deleted, so staging survives and the next update's clean-up
        // of it throws — you cannot update twice without restarting in between.
        var s = Win();
        Assert.Contains($"-WorkingDirectory '{WorkDir}'", s);
    }

    [Fact]
    public void Unix_starts_the_app_in_the_install_directory_not_the_staging_one()
    {
        var s = Nix();
        Assert.Contains($"cd '{WorkDir}'", s);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_remove_the_staging_directory_and_then_themselves(bool windows)
    {
        // ~100 MB of unpacked release per update, otherwise left in temp until the next one.
        var s = windows ? Win() : Nix();
        Assert.Contains(StageRoot, s);
        Assert.Contains(ScriptPath, s);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_app_is_relaunched_before_staging_is_removed(bool windows)
    {
        // Order matters: staging still holds the staged exe the copy reads from, and the relaunch
        // must not race a directory being deleted out from under it.
        var s = windows ? Win() : Nix();
        var relaunch = windows ? s.IndexOf("Start-Process", StringComparison.Ordinal)
                               : s.IndexOf($"cd '{WorkDir}'", StringComparison.Ordinal);
        var cleanup = s.IndexOf(StageRoot + "'", StringComparison.Ordinal);

        Assert.True(relaunch >= 0 && cleanup >= 0);
        Assert.True(relaunch < cleanup, "the relaunch must come before the staging clean-up");
    }
}
