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

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Pans() => new()
    {
        ["0x40000000"] = new Dictionary<string, string> { ["bandwidth"] = "0.050000", ["center"] = "144.200000" },
        ["0x40000001"] = new Dictionary<string, string> { ["bandwidth"] = "0.100000", ["center"] = "432.150000" },
    };

    [Fact]
    public void Full_records_each_panadapters_width_and_centre_last_and_once_per_panadapter()
    {
        var t = TwoSlices();
        t.Merge("slice 0 pan=0x40000000");
        t.Merge("slice 1 pan=0x40000001");
        var (_, lines) = SliceCapture.CaptureAll(t, new Dictionary<string, string>(), full: true, When, Pans());
        Assert.Equal(new[]
        {
            "display pan set {panA} bandwidth=0.050000", "display pan set {panA} center=144.200000",
            "display pan set {panB} bandwidth=0.100000", "display pan set {panB} center=432.150000",
        }, lines.Skip(lines.Count - 4));

        // two slices in one panadapter: one pair of lines, through the first of them
        t.Merge("slice 1 pan=0x40000000");
        var shared = SliceCapture.CaptureAll(t, new Dictionary<string, string>(), full: true, When, Pans()).Lines;
        Assert.Equal(2, shared.Count(l => l.StartsWith("display pan set")));
        Assert.All(shared.Where(l => l.StartsWith("display pan set")), l => Assert.Contains("{panA}", l));
    }

    [Fact]
    public void Basic_and_unknown_panadapters_leave_the_scope_out()
    {
        var t = TwoSlices();
        t.Merge("slice 0 pan=0x40000000");
        Assert.DoesNotContain(SliceCapture.CaptureAll(t, new Dictionary<string, string>(), full: false, When, Pans()).Lines, l => l.StartsWith("display pan"));
        Assert.DoesNotContain(SliceCapture.CaptureAll(t, new Dictionary<string, string>(), full: true, When, pans: null).Lines, l => l.StartsWith("display pan"));
        t.Merge("slice 0 active=1"); t.Merge("slice 1 active=0");
        var single = SliceCapture.Capture(t, new Dictionary<string, string>(), full: true, When, Pans()).Lines;
        Assert.Equal(new[] { "display pan set {pan} bandwidth=0.050000", "display pan set {pan} center=144.200000" }, single.Skip(single.Count - 2));
    }

    [Fact]
    public void Pan_by_letter_resolves_to_that_slices_panadapter()
    {
        var t = TwoSlices();
        t.Merge("slice 0 pan=0x40000000");
        t.Merge("slice 1 pan=0x40000001");
        Assert.Equal("display pan set 0x40000001 bandwidth=0.1", CommandSequence.Substitute("display pan set {panB} bandwidth=0.1", t));
        Assert.Equal("display pan set 0x40000001 band=20", CommandSequence.Substitute("display pan set {pan} band=20", t));   // B is active
        Assert.Contains("no slice C", Assert.Throws<SequenceException>(() => CommandSequence.Substitute("x {panC}", t)).Message);
    }
}
