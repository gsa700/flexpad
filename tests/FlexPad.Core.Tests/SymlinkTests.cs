using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

/// <summary>
/// Regression cover for the <c>~/.local/bin/flexpad</c> shortcut. The installer probed the link
/// with <see cref="File.ResolveLinkTarget(string,bool)"/> before deciding whether to create it, and
/// that call throws <see cref="FileNotFoundException"/> when nothing is at the path — the ordinary
/// first-install case. Being an <see cref="IOException"/>, it was caught by the handler meant for a
/// failed *creation*, so the create was skipped and the symlink was never made on Linux, on that
/// launch or any later one. Found on the CM5: the .desktop entry and icon were present, the symlink
/// was not.
/// </summary>
public class SymlinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "w2-symlink-tests", Guid.NewGuid().ToString("N"));

    public SymlinkTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private string Path_(params string[] parts) => Path.Combine([_dir, .. parts]);

    // ---- ResolveTarget: probing must be total, never an exception ----

    [Fact]
    public void ResolveTarget_returns_null_for_a_missing_path()
    {
        // The regression. This threw before, and the throw was mistaken for "creation failed".
        Assert.Null(Symlink.ResolveTarget(Path_("no-link-here")));
    }

    [Fact]
    public void ResolveTarget_returns_null_when_the_parent_directory_is_missing()
    {
        // ~/.local/bin does not exist on a fresh account, so this is a real first-install shape.
        Assert.Null(Symlink.ResolveTarget(Path_("nested", "deep", "flexpad")));
    }

    [Fact]
    public void ResolveTarget_returns_null_for_a_regular_file()
    {
        var path = Path_("just-a-file");
        File.WriteAllText(path, "x");
        Assert.Null(Symlink.ResolveTarget(path));
    }

    [Fact]
    public void The_bcl_probe_throws_an_IOException_for_a_missing_path()
    {
        // Why Symlink.ResolveTarget exists at all, pinned so the reason survives. If a future
        // runtime stops throwing here, the wrapper can be simplified — and this test will say so.
        var ex = Assert.Throws<FileNotFoundException>(
            () => File.ResolveLinkTarget(Path_("no-link-here"), returnFinalTarget: false));
        Assert.IsAssignableFrom<IOException>(ex);
    }

    [UnixFact]
    public void ResolveTarget_reports_the_target_of_a_link()
    {
        var target = Path_("app");
        var link = Path_("flexpad");
        File.WriteAllText(target, "x");
        File.CreateSymbolicLink(link, target);

        Assert.Equal(target, Symlink.ResolveTarget(link));
    }

    [UnixFact]
    public void ResolveTarget_reports_the_target_of_a_dangling_link()
    {
        var target = Path_("app");
        var link = Path_("flexpad");
        File.WriteAllText(target, "x");
        File.CreateSymbolicLink(link, target);
        File.Delete(target);

        // Deliberately no assertion about File.Exists(link) here: measured on .NET 10 / linux-arm64
        // it returns true for a dangling link, though the long-standing comment in InstallService
        // assumed false. Since neither answer is depended on — every caller asks both questions —
        // pinning it would only invite a false alarm on a runtime that decides the other way.
        Assert.Equal(target, Symlink.ResolveTarget(link));     // the link itself still knows
    }

    // ---- Ensure: the behaviour the installer actually depends on ----

    [UnixFact]
    public void Ensure_creates_the_link_when_none_exists()
    {
        // The symptom seen on the Pi: nothing at the path, and nothing ever created.
        var target = Path_("app");
        var link = Path_("flexpad");
        File.WriteAllText(target, "x");

        Assert.True(Symlink.Ensure(link, target));
        Assert.Equal(target, Symlink.ResolveTarget(link));
    }

    [UnixFact]
    public void Ensure_creates_the_missing_parent_directory()
    {
        var target = Path_("app");
        var link = Path_("local", "bin", "flexpad");
        File.WriteAllText(target, "x");

        Assert.True(Symlink.Ensure(link, target));
        Assert.Equal(target, Symlink.ResolveTarget(link));
    }

    [UnixFact]
    public void Ensure_replaces_a_link_aimed_elsewhere()
    {
        // An install directory that moved: the stale link must be repointed, not left.
        var old = Path_("old-app");
        var current = Path_("new-app");
        var link = Path_("flexpad");
        File.WriteAllText(old, "x");
        File.WriteAllText(current, "x");
        File.CreateSymbolicLink(link, old);

        Assert.True(Symlink.Ensure(link, current));
        Assert.Equal(current, Symlink.ResolveTarget(link));
    }

    [UnixFact]
    public void Ensure_leaves_an_already_correct_link_alone()
    {
        // Runs on every launch, so a correct link must be a no-op rather than a delete/recreate
        // window in which the terminal command briefly does not exist.
        var target = Path_("app");
        var link = Path_("flexpad");
        File.WriteAllText(target, "x");
        Symlink.Ensure(link, target);

        Assert.False(Symlink.Ensure(link, target));
        Assert.Equal(target, Symlink.ResolveTarget(link));
    }

    [UnixFact]
    public void Ensure_replaces_a_regular_file_in_the_way()
    {
        var target = Path_("app");
        var link = Path_("flexpad");
        File.WriteAllText(target, "x");
        File.WriteAllText(link, "something else entirely");

        Assert.True(Symlink.Ensure(link, target));
        Assert.Equal(target, Symlink.ResolveTarget(link));
    }

    [UnixFact]
    public void Ensure_replaces_a_dangling_link()
    {
        // File.Exists reports false for these, so a check that only asked File.Exists would try to
        // create over an existing link and throw.
        var gone = Path_("gone");
        var target = Path_("app");
        var link = Path_("flexpad");
        File.WriteAllText(gone, "x");
        File.WriteAllText(target, "x");
        File.CreateSymbolicLink(link, gone);
        File.Delete(gone);

        Assert.True(Symlink.Ensure(link, target));
        Assert.Equal(target, Symlink.ResolveTarget(link));
    }
}
