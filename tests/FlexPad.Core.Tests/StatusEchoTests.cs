using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class StatusEchoTests
{
    [Theory]
    [InlineData("slice set 1 agc_threshold=52", "slice 1 agc_threshold=52")]
    [InlineData("slice s 0 audio_level=20 audio_pan=50 audio_mute=0", "slice 0 audio_level=20 audio_pan=50 audio_mute=0")]
    [InlineData("  SLICE SET 3 rxant=XVTB txant=XVTB ", "slice 3 rxant=XVTB txant=XVTB")]
    [InlineData("display pan set 0x40000001 average=69", "display pan 0x40000001 average=69")]
    [InlineData("display pan s 0x40000000 bandwidth=0.050000", "display pan 0x40000000 bandwidth=0.050000")]
    [InlineData("transmit set rfpower=33", "transmit rfpower=33")]
    [InlineData("filt 1 -2900 -150", "slice 1 filter_lo=-2900 filter_hi=-150")]
    [InlineData("slice tune 0 3.925", "slice 0 RF_frequency=3.925000")]          // the radio's own six-decimal form
    [InlineData("slice t 1 144.200000", "slice 1 RF_frequency=144.200000")]
    public void A_plain_setting_becomes_the_status_the_radio_sends_everyone_else(string command, string expected) =>
        Assert.Equal(expected, StatusEcho.For(command));

    [Theory]
    [InlineData("slice set 0 active=1")]                 // moves the flag off another slice: the radio reports it
    [InlineData("slice set 1 tx=1")]
    [InlineData("display pan set 0x40000000 band=20")]   // the radio retunes from its own memory
    [InlineData("slice tune 0 fourteen")]                // not a frequency: let the radio's reply speak
    [InlineData("slice tune 0")]
    [InlineData("slice create freq=14.1 ant=ANT1 mode=USB")]
    [InlineData("slice remove 1")]
    [InlineData("sub slice all")]
    [InlineData("info")]
    public void Anything_else_is_left_to_the_radio(string command) => Assert.Null(StatusEcho.For(command));

    [Fact]
    public void The_flags_are_dropped_but_the_rest_of_the_line_is_kept()
    {
        Assert.Equal("slice 0 mode=USB", StatusEcho.For("slice set 0 active=1 mode=USB"));
    }

    [Fact]
    public void A_tune_moves_the_slice_in_our_own_table_so_the_knob_builds_on_it()
    {
        // 2026-09-23: a button tuned the radio to 3.925 but no status came back on that session; the
        // status line stayed on 20 m and the first knob tick sent the radio back to 20 m.
        var t = new SliceTable();
        t.Merge("slice 0 in_use=1 index_letter=A active=1 RF_frequency=14.250000 step=100");
        Assert.True(t.Merge(StatusEcho.For("slice tune 0 3.925")!));
        Assert.Equal("3.925000", t.Get("0")!["RF_frequency"]);
        Assert.Equal("A", t.Get("0")!["index_letter"]);
    }

    [Fact]
    public void It_merges_into_a_slice_table_like_any_status_line()
    {
        var t = new SliceTable();
        t.Merge("slice 1 in_use=1 index_letter=B agc_threshold=60 audio_level=15");
        Assert.True(t.Merge(StatusEcho.For("slice set 1 agc_threshold=50 audio_level=22")!));
        Assert.Equal("50", t.Get("1")!["agc_threshold"]);
        Assert.Equal("22", t.Get("1")!["audio_level"]);
        Assert.Equal("B", t.Get("1")!["index_letter"]);
    }
}
