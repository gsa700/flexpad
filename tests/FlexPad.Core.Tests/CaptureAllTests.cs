using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class CaptureAllTests
{
    private static readonly DateTime When = new(2026, 9, 18, 9, 30, 0);

    private static SliceTable TwoSlices()
    {
        var t = new SliceTable();
        t.Merge("slice 0 in_use=1 index_letter=A RF_frequency=3.925000 mode=LSB rxant=ANT1 txant=ANT1 filter_lo=-2900 filter_hi=-150 active=0 tx=1 step=100");
        t.Merge("slice 1 in_use=1 index_letter=B RF_frequency=432.100000 mode=USB rxant=XVTB txant=XVTB filter_lo=150 filter_hi=2900 active=1 tx=0 step=10");
        t.Merge("slice 2 in_use=0 index_letter=C RF_frequency=7.100000 mode=LSB");
        return t;
    }

    [Fact]
    public void Basic_names_the_letters_then_each_slice_then_tx_and_active()
    {
        var (label, lines) = SliceCapture.CaptureAll(TwoSlices(), new Dictionary<string, string>(), full: false, When);
        Assert.Equal("432.100 USB +1", label);   // the active slice, plus one more
        Assert.Equal(new[]
        {
            "# captured 2 slices (A, B) on 2026-09-18 09:30",
            "# opens or closes slices until exactly these exist, then sets each one up",
            "slices A B",
            "# slice A",
            "slice tune {A} 3.925000",
            "slice set {A} mode=LSB",
            "slice set {A} rxant=ANT1 txant=ANT1",
            "filt {A} -2900 -150",
            "# slice B",
            "slice tune {B} 432.100000",
            "slice set {B} mode=USB",
            "slice set {B} rxant=XVTB txant=XVTB",
            "filt {B} 150 2900",
            "slice set {A} tx=1",
            "slice set {B} active=1",
        }, lines);
    }

    [Fact]
    public void Full_adds_per_slice_dsp_and_the_power_lines_once_at_the_end()
    {
        var tx = new Dictionary<string, string> { ["rfpower"] = "75", ["tunepower"] = "10" };
        var (_, lines) = SliceCapture.CaptureAll(TwoSlices(), tx, full: true, When);
        Assert.Contains("slice set {A} step=100", lines);
        Assert.Contains("slice set {B} step=10", lines);
        Assert.Equal(1, lines.Count(l => l == "transmit set rfpower=75"));
        Assert.Equal("transmit set tunepower=10", lines[^1]);
    }

    [Fact]
    public void The_capture_replays_onto_a_radio_with_one_slice()
    {
        var (_, lines) = SliceCapture.CaptureAll(TwoSlices(), new Dictionary<string, string>(), full: false, When);
        var radio = new FakeRadio("A");
        var errors = new List<string>();
        Assert.True(CommandSequence.Run(lines, radio.Slices, radio.Send, stopOnError: true, errors.Add, _ => { }));
        Assert.Empty(errors);
        Assert.Equal("AB", radio.Letters());
        Assert.Contains("slice tune 1 432.100000", radio.Sent);
        Assert.Contains("slice set 1 active=1", radio.Sent);
    }

    [Fact]
    public void One_slice_gets_no_plus_suffix_and_nothing_open_is_an_error()
    {
        var t = new SliceTable();
        t.Merge("slice 0 in_use=1 index_letter=A RF_frequency=14.250000 mode=USB active=1 tx=1");
        Assert.Equal("14.250 USB", SliceCapture.CaptureAll(t, new Dictionary<string, string>(), false, When).Label);
        Assert.Throws<SequenceException>(() => SliceCapture.CaptureAll(new SliceTable(), new Dictionary<string, string>(), false, When));
    }
}
