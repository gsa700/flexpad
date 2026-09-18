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
        // ...and then makes that slice the active one, so the front panel and the knob follow it.
        Assert.Equal(new[] { "slice tune 0 7.200", "slice set 0 mode=LSB", "filt 0 -2900 -100", "slice set 0 active=1" }, sent);
    }

    [Fact]
    public void No_activate_when_the_pinned_slice_is_already_active_or_the_button_is_unpinned()
    {
        var t = AandB_withBActive();
        var sent = new List<string>();
        CommandSequence.Run(new[] { "slice tune {slice} 432.100" }, t, c => { sent.Add(c); return (0, ""); },
            stopOnError: true, report: null, sleep: _ => { }, target: "B");
        Assert.Equal(new[] { "slice tune 1 432.100" }, sent);

        sent.Clear();
        CommandSequence.Run(new[] { "slice tune {slice} 432.100" }, t, c => { sent.Add(c); return (0, ""); },
            stopOnError: true, report: null, sleep: _ => { });
        Assert.Equal(new[] { "slice tune 1 432.100" }, sent);
    }

    [Fact]
    public void A_button_that_never_addresses_its_own_slice_does_not_pull_the_focus()
    {
        // Every button runs on A by default now, so "TX -> B" carries target A without meaning anything by it.
        var t = AandB_withBActive();
        var sent = new List<string>();
        CommandSequence.Run(new[] { "# uses {slice} only in a comment", "slice set {B} tx=1" }, t,
            c => { sent.Add(c); return (0, ""); }, stopOnError: true, report: null, sleep: _ => { }, target: "A");
        Assert.Equal(new[] { "slice set 1 tx=1" }, sent);
    }

    [Fact]
    public void A_run_that_stops_on_an_error_leaves_the_focus_alone()
    {
        var t = AandB_withBActive();
        var sent = new List<string>();
        var ok = CommandSequence.Run(new[] { "slice tune {slice} 999" }, t,
            c => { sent.Add(c); return (0x50000001, "out of range"); },
            stopOnError: true, report: _ => { }, sleep: _ => { }, target: "A");
        Assert.False(ok);
        Assert.DoesNotContain("slice set 0 active=1", sent);
    }

    [Fact]
    public void A_pinned_button_that_opens_its_own_slice_activates_it_afterwards()
    {
        var radio = new FakeRadio("A");
        var ok = CommandSequence.Run(new[] { "slices A B", "slice tune {slice} 432.100" }, radio.Slices, radio.Send,
            stopOnError: true, report: null, sleep: _ => { }, target: "B");
        Assert.True(ok);
        Assert.Equal("slice set 1 active=1", radio.Sent.Last());
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
