using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class FlexProtocolTests
{
    // Lines below are verbatim from a FLEX-8600M on SmartSDR v4.2, 2026-09-14.

    [Fact]
    public void Response_with_text_parses_sequence_hex_code_and_text()
    {
        var p = FlexProtocol.Parse("R2|0|SmartSDR-MB=4.2.20.41343#PSoC-MBTRX=1.0.6.0");
        Assert.Equal(LineKind.Response, p.Kind);
        Assert.Equal(2, p.Sequence);
        Assert.Equal(0, p.Code);
        Assert.StartsWith("SmartSDR-MB=", p.Text);
    }

    [Fact]
    public void Response_error_code_is_hex()
    {
        var p = FlexProtocol.Parse("R7|5000002D|Bad field 'lock=0'");
        Assert.Equal(0x5000002D, p.Code);
        Assert.Equal("Bad field 'lock=0'", p.Text);
    }

    [Fact]
    public void Response_without_text_has_empty_text()
    {
        var p = FlexProtocol.Parse("R1|0|");
        Assert.Equal(0, p.Code);
        Assert.Equal("", p.Text);
    }

    [Theory]
    [InlineData("V1.4.0.0", LineKind.Version, "1.4.0.0")]
    [InlineData("H5C012CB3", LineKind.Handle, "5C012CB3")]
    [InlineData("M10000001|Client connected from IP 10.0.1.172", LineKind.Message, "Client connected from IP 10.0.1.172")]
    [InlineData("S5C012CB3|slice 0 in_use=1 mode=USB", LineKind.Status, "slice 0 in_use=1 mode=USB")]
    public void Other_kinds_carry_their_payload(string line, LineKind kind, string text)
    {
        var p = FlexProtocol.Parse(line);
        Assert.Equal(kind, p.Kind);
        Assert.Equal(text, p.Text);
    }

    [Fact]
    public void KeyValues_ignores_bare_tokens()
    {
        var kv = FlexProtocol.KeyValues("in_use=1 RF_frequency=14.250000 bare mode=USB");
        Assert.Equal(3, kv.Count);
        Assert.Equal("14.250000", kv["RF_frequency"]);
    }

    [Theory]
    [InlineData(14_250_000, "14.250000")]
    [InlineData(144_200_000, "144.200000")]
    [InlineData(3_860_000, "3.860000")]
    [InlineData(0, "0.000000")]
    public void FormatMhz_uses_six_decimals_invariant(long hz, string expected) =>
        Assert.Equal(expected, FlexProtocol.FormatMhz(hz));

    [Fact]
    public void ParseMhz_round_trips_the_radio_format()
    {
        Assert.Equal(14_260_000, FlexProtocol.ParseMhz("14.260000"));
        Assert.Null(FlexProtocol.ParseMhz("?"));
        Assert.Null(FlexProtocol.ParseMhz(null));
    }
}

public class SliceTableTests
{
    private static SliceTable TwoSlices()
    {
        var t = new SliceTable();
        t.Merge("slice 0 in_use=1 active=1 tx=0 index_letter=A RF_frequency=14.250000 mode=USB");
        t.Merge("slice 1 in_use=1 active=0 tx=1 index_letter=B RF_frequency=432.100000 mode=USB");
        return t;
    }

    [Fact]
    public void Merge_is_incremental_like_the_radio()
    {
        var t = TwoSlices();
        // A retune sends RF_frequency alone; everything else must survive.
        t.Merge("slice 0 RF_frequency=14.260000");
        var s = t.Get("0")!;
        Assert.Equal("14.260000", s["RF_frequency"]);
        Assert.Equal("USB", s["mode"]);
        Assert.Equal("A", s["index_letter"]);
    }

    [Fact]
    public void Active_tx_and_letter_lookups()
    {
        var t = TwoSlices();
        Assert.Equal("0", t.Active());
        Assert.Equal("1", t.Tx());
        Assert.Equal("1", t.ByLetter("B"));
        Assert.Null(t.ByLetter("C"));
        Assert.Equal(new[] { "0", "1" }, t.Live());
    }

    [Fact]
    public void A_removed_slice_is_not_live()
    {
        var t = TwoSlices();
        t.Merge("slice 1 in_use=0");
        Assert.Equal(new[] { "0" }, t.Live());
        Assert.Null(t.Tx());
    }

    [Fact]
    public void Non_slice_status_is_rejected()
    {
        var t = new SliceTable();
        Assert.False(t.Merge("transmit rfpower=85 tunepower=42"));
    }

    [Fact]
    public void Get_returns_a_copy()
    {
        var t = TwoSlices();
        var copy = (Dictionary<string, string>)t.Get("0")!;
        copy["mode"] = "CW";
        Assert.Equal("USB", t.Get("0")!["mode"]);
    }
}
