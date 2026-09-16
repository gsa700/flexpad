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

public class BandChangeTests
{
    private static readonly Dictionary<string, string> Xvtrs = new(StringComparer.OrdinalIgnoreCase) { ["2m"] = "0", ["70cm"] = "1" };

    [Fact]
    public void One_pan_band_line_per_band_with_the_radios_codes()
    {
        var (set, skipped) = BandSet.GenerateBandChange(new[] { "70cm", "20m", "160m", "2m" }, Xvtrs, hotkeys: false);
        Assert.Empty(skipped);
        Assert.Equal(new[] { "160m", "20m", "2m", "70cm" }, set.Select(b => b.Label));
        Assert.Equal(new[] { "display pan set {pan} band=160" }, set[0].Lines);
        Assert.Equal(new[] { "display pan set {pan} band=20" }, set[1].Lines);
        Assert.Equal(new[] { "display pan set {pan} band=x0" }, set[2].Lines);
        Assert.Equal(new[] { "display pan set {pan} band=x1" }, set[3].Lines);
        Assert.Equal(BandSet.TransverterColor, set[2].Color);
        Assert.Null(set[1].Color);
    }

    [Fact]
    public void Every_hf_band_has_a_code()
    {
        var (set, skipped) = BandSet.GenerateBandChange(BandSet.Bands.Select(b => b.Name), Xvtrs, hotkeys: true);
        Assert.Empty(skipped);
        Assert.Equal(12, set.Count);
        Assert.Equal("F1", set[0].Key);
        Assert.Equal("F12", set[11].Key);
        Assert.All(set, b => Assert.Matches(@"^display pan set \{pan\} band=(x?\d+)$", Assert.Single(b.Lines)));
    }

    [Fact]
    public void Transverter_band_the_radio_does_not_know_is_skipped_not_broken()
    {
        var (set, skipped) = BandSet.GenerateBandChange(new[] { "2m", "70cm", "6m" }, new Dictionary<string, string> { ["2M"] = "3" }, hotkeys: true);
        Assert.Equal(new[] { "70cm" }, skipped);
        Assert.Equal(new[] { "6m", "2m" }, set.Select(b => b.Label));
        Assert.Equal("display pan set {pan} band=x3", set[1].Lines[0]);   // name matched regardless of case
        Assert.Equal(new[] { "F1", "F2" }, set.Select(b => b.Key));      // no gap for the skipped band
    }
}

public class BroadcastSetTests
{
    [Fact]
    public void Broadcast_bands_are_am_recipes_on_the_hf_antenna_in_frequency_order()
    {
        var set = BandSet.GenerateBroadcast(new[] { "CB", "AM BC", "49m BC", "WWV" }, "ANT2", hotkeys: false);
        Assert.Equal(new[] { "AM BC", "49m BC", "WWV", "CB" }, set.Select(b => b.Label));
        Assert.Contains("slice tune {slice} 6.000000", set[1].Lines);
        Assert.Contains("slice set {slice} mode=AM", set[1].Lines);
        Assert.Contains("slice set {slice} rxant=ANT2 txant=ANT2", set[1].Lines);
        Assert.Contains("filt {slice} -3000 3000", set[1].Lines);
        Assert.Contains("slice tune {slice} 27.185000", set[3].Lines);
        Assert.All(set, b => Assert.Equal(BandSet.BroadcastColor, b.Color));
    }

    [Fact]
    public void Broadcast_labels_do_not_collide_with_amateur_labels()
    {
        var amateur = BandSet.Bands.Select(b => b.Name).ToHashSet();
        Assert.All(BandSet.Broadcast, b => Assert.DoesNotContain(b.Name, amateur));
        Assert.Equal(BandSet.Broadcast.Length, BandSet.Broadcast.Select(b => b.Name).Distinct().Count());
        Assert.True(BandSet.Broadcast.Zip(BandSet.Broadcast.Skip(1)).All(p => p.First.PhoneMhz < p.Second.PhoneMhz));
    }
}
