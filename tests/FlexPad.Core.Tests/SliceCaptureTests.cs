using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class SliceCaptureTests
{
    private static SliceTable Radio()
    {
        // Attribute values as the 8600M reported them on 2026-09-14.
        var t = new SliceTable();
        t.Merge("slice 0 in_use=1 active=1 tx=1 index_letter=A RF_frequency=14.260000 mode=USB rxant=ANT1 txant=ANT1 " +
                "filter_lo=100 filter_hi=2900 step=100 agc_mode=med agc_threshold=30 nr=0 nr_level=50 nb=0 nb_level=50 " +
                "wnb=0 wnb_level=90 anf=0 rfgain=32 dax=1");
        return t;
    }

    private static readonly Dictionary<string, string> Tx = new() { ["rfpower"] = "85", ["tunepower"] = "42" };

    [Fact]
    public void Basic_is_the_memory_channel_plus_antennas()
    {
        var (label, lines) = SliceCapture.Capture(Radio(), Tx, full: false, now: new DateTime(2026, 9, 14, 18, 51, 0));
        Assert.Equal("14.260 USB", label);
        Assert.Equal(new[]
        {
            "# captured from slice A on 2026-09-14 18:51",
            "slice tune {slice} 14.260000",
            "slice set {slice} mode=USB",
            "slice set {slice} rxant=ANT1 txant=ANT1",
            "filt {slice} 100 2900",
        }, lines);
    }

    [Fact]
    public void Full_adds_dsp_and_tx_power_with_the_verified_keywords()
    {
        var (_, lines) = SliceCapture.Capture(Radio(), Tx, full: true);
        Assert.Contains("slice set {slice} step=100", lines);
        Assert.Contains("slice set {slice} agc_mode=med agc_threshold=30", lines);
        Assert.Contains("slice set {slice} wnb=0 wnb_level=90", lines);
        Assert.Contains("slice set {slice} rfgain=32", lines);
        Assert.Contains("slice set {slice} dax=1", lines);
        Assert.Contains("transmit set rfpower=85", lines);
        Assert.Contains("transmit set tunepower=42", lines);
        Assert.Contains(lines, l => l.StartsWith("slice set {slice} audio_level="));   // volume, pan and mute, since 0.14.1
        Assert.Equal(16, lines.Count);
    }

    [Fact]
    public void Full_without_transmit_status_omits_power_lines()
    {
        var (_, lines) = SliceCapture.Capture(Radio(), new Dictionary<string, string>(), full: true);
        Assert.DoesNotContain(lines, l => l.StartsWith("transmit"));
    }

    [Fact]
    public void No_active_slice_is_an_error()
    {
        Assert.Throws<SequenceException>(() => SliceCapture.Capture(new SliceTable(), Tx, false));
    }
}

public class DiscoveryTests
{
    [Fact]
    public void Parses_the_radios_broadcast_after_the_vita_header()
    {
        var payload = "discovery_protocol_version=3.1.0.4 model=FLEX-8600M serial=1926-1213-8601-0330 version=4.2.20.41343 " +
                      "nickname=8600M callsign=AB0R ip=10.0.1.106 port=4992 status=Available";
        var packet = new byte[28].Concat(System.Text.Encoding.ASCII.GetBytes(payload)).ToArray();
        var r = Discovery.Parse(packet);
        Assert.NotNull(r);
        Assert.Equal("10.0.1.106", r!.Ip);
        Assert.Equal("FLEX-8600M", r.Model);
        Assert.Equal("8600M", r.Nickname);
        Assert.Equal("AB0R", r.Callsign);
        Assert.Equal("Available", r.Status);
    }

    [Fact]
    public void Rejects_short_or_foreign_packets()
    {
        Assert.Null(Discovery.Parse(new byte[10]));
        Assert.Null(Discovery.Parse(new byte[28].Concat(System.Text.Encoding.ASCII.GetBytes("hello=world")).ToArray()));
    }
}
