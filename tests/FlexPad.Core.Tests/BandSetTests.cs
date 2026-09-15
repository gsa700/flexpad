using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class BandSetTests
{
    [Fact]
    public void Generates_in_table_order_with_the_right_ports_and_modes()
    {
        var set = BandSet.Generate(new[] { "70cm", "20m", "40m", "2m" }, "ANT1", "XVTA", "XVTB", cw: false, hotkeys: false);
        Assert.Equal(new[] { "40m", "20m", "2m", "70cm" }, set.Select(b => b.Label));
        Assert.Contains("slice set {slice} mode=LSB", set[0].Lines);
        Assert.Contains("slice set {slice} rxant=ANT1 txant=ANT1", set[0].Lines);
        Assert.Contains("filt {slice} -2900 -100", set[0].Lines);
        Assert.Contains("slice tune {slice} 144.200000", set[2].Lines);
        Assert.Contains("slice set {slice} rxant=XVTA txant=XVTA", set[2].Lines);
        Assert.Contains("slice set {slice} rxant=XVTB txant=XVTB", set[3].Lines);
        Assert.Equal(BandSet.TransverterColor, set[3].Color);
        Assert.Null(set[0].Color);
        Assert.All(set, b => Assert.Null(b.Key));
    }

    [Fact]
    public void Cw_uses_cw_spots_mode_filter_and_labels()
    {
        var set = BandSet.Generate(new[] { "20m" }, "ANT1", "XVTA", "XVTB", cw: true, hotkeys: false);
        var b = Assert.Single(set);
        Assert.Equal("20m CW", b.Label);
        Assert.Contains("slice tune {slice} 14.030000", b.Lines);
        Assert.Contains("slice set {slice} mode=CW", b.Lines);
        Assert.Contains("filt {slice} 350 850", b.Lines);
    }

    [Fact]
    public void Hotkeys_run_f1_upward_in_order_and_stop_at_f12()
    {
        var all = BandSet.Bands.Select(b => b.Name);
        var set = BandSet.Generate(all, "ANT1", "XVTA", "XVTB", cw: false, hotkeys: true);
        Assert.Equal(12, set.Count);
        Assert.Equal("F1", set[0].Key);
        Assert.Equal("F12", set[11].Key);
    }

    [Fact]
    public void Thirty_metres_has_no_phone_and_falls_back_to_cw()
    {
        var set = BandSet.Generate(new[] { "30m" }, "ANT2", "XVTA", "XVTB", cw: false, hotkeys: false);
        Assert.Contains("slice set {slice} mode=CW", set[0].Lines);
        Assert.Contains("slice tune {slice} 10.120000", set[0].Lines);
    }

    [Fact]
    public void Unknown_names_are_ignored()
    {
        Assert.Empty(BandSet.Generate(new[] { "23cm" }, "ANT1", "XVTA", "XVTB", false, false));
    }
}
