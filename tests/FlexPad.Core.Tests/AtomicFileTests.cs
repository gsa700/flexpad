using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "w2-atomicfile-tests", Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void Write_creates_file_with_content()
    {
        var path = Path.Combine(_dir, "config.json");
        AtomicFile.WriteAllText(path, "hello");
        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void Write_fully_replaces_existing_content()
    {
        var path = Path.Combine(_dir, "config.json");
        AtomicFile.WriteAllText(path, "a-long-original-value");
        AtomicFile.WriteAllText(path, "new");
        Assert.Equal("new", File.ReadAllText(path));   // no leftover tail from the longer original
    }

    [Fact]
    public void Write_leaves_no_temp_file_behind()
    {
        var path = Path.Combine(_dir, "config.json");
        AtomicFile.WriteAllText(path, "x");
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_creates_missing_directory()
    {
        var path = Path.Combine(_dir, "nested", "deep", "config.json");
        AtomicFile.WriteAllText(path, "y");
        Assert.Equal("y", File.ReadAllText(path));
    }

    [Fact]
    public void Backup_copies_existing_file_to_bak()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "corrupt-but-precious");
        AtomicFile.Backup(path);
        Assert.Equal("corrupt-but-precious", File.ReadAllText(path + ".bak"));
        Assert.True(File.Exists(path));   // original left in place
    }

    [Fact]
    public void Backup_is_a_noop_when_source_is_missing()
    {
        var path = Path.Combine(_dir, "does-not-exist.json");
        AtomicFile.Backup(path);   // must not throw
        Assert.False(File.Exists(path + ".bak"));
    }
}
