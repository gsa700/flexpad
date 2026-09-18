using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

/// <summary>
/// A button pinned to a slice letter. The case that started it (David, 2026-09-19): a memory made
/// on slice A, then a second slice opened and became active, and the memory retuned B instead.
/// </summary>
public class TargetSliceTests
{
    private static SliceTable AandB_withBActive()
    {
        var t = new SliceTable();
        t.Merge("slice 0 in_use=1 index_letter=A active=0 tx=1 pan=0x40000000");
        t.Merge("slice 1 in_use=1 index_letter=B active=1 tx=0 pan=0x40000001");
        return t;
    }

    [Fact]
    public void Unpinned_follows_the_active_slice_as_before()
    {
        var t = AandB_withBActive();
        Assert.Equal("slice tune 1 14.250", CommandSequence.Substitute("slice tune {slice} 14.250", t));
        Assert.Equal("display pan set 0x40000001 band=20", CommandSequence.Substitute("display pan set {pan} band=20", t));
    }

    [Fact]
    public void Pinned_to_A_lands_on_A_while_B_is_active()
    {
        var t = AandB_withBActive();
        Assert.Equal("slice tune 0 14.250", CommandSequence.Substitute("slice tune {slice} 14.250", t, "A"));
        Assert.Equal("display pan set 0x40000000 band=20", CommandSequence.Substitute("display pan set {pan} band=20", t, "A"));
        // letters and {tx} are not affected by the pin
        Assert.Equal("slice set 1 tx=1", CommandSequence.Substitute("slice set {B} tx=1", t, "A"));
        Assert.Equal("filt 0 1 2", CommandSequence.Substitute("filt {tx} 1 2", t, "B"));
    }

    [Fact]
    public void A_whole_button_runs_on_its_slice()
    {
        var t = AandB_withBActive();
        var sent = new List<string>();
        var ok = CommandSequence.Run(
            new[] { "slice tune {slice} 7.200", "slice set {slice} mode=LSB", "filt {slice} -2900 -100" },
            t, c => { sent.Add(c); return (0, ""); }, stopOnError: true, report: null, sleep: _ => { }, target: "A");
        Assert.True(ok);
        Assert.Equal(new[] { "slice tune 0 7.200", "slice set 0 mode=LSB", "filt 0 -2900 -100" }, sent);
    }

    [Fact]
    public void A_pinned_slice_that_is_not_open_says_so_and_sends_nothing()
    {
        var t = AandB_withBActive();
        var sent = new List<string>();
        var errors = new List<string>();
        var ok = CommandSequence.Run(new[] { "slice tune {slice} 7.200" }, t,
            c => { sent.Add(c); return (0, ""); }, stopOnError: true, errors.Add, _ => { }, target: "C");
        Assert.False(ok);
        Assert.Empty(sent);
        Assert.Contains("runs on slice C, which is not open", Assert.Single(errors));
    }
}
