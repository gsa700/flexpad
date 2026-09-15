using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class ButtonBuilderTests
{
    [Fact]
    public void Lists_come_from_the_radio_when_present()
    {
        var slice = new Dictionary<string, string>
        {
            ["mode_list"] = "LSB,USB,AM,CW,DIGL,DIGU,SAM,FM,NFM,DFM,RTTY",
            ["ant_list"] = "ANT1,ANT2,RX_A,RX_B,XVTA,XVTB",
            ["tx_ant_list"] = "ANT1,ANT2,XVTA,XVTB",
        };
        Assert.Equal(11, ButtonBuilder.ListFrom(slice, "mode_list", ButtonBuilder.DefaultModes).Count);
        Assert.Equal(new[] { "ANT1", "ANT2", "RX_A", "RX_B", "XVTA", "XVTB" }, ButtonBuilder.ListFrom(slice, "ant_list", ButtonBuilder.DefaultAntennas));
        Assert.Equal(4, ButtonBuilder.ListFrom(slice, "tx_ant_list", ButtonBuilder.DefaultAntennas).Count);
    }

    [Fact]
    public void Lists_fall_back_without_a_slice()
    {
        Assert.Equal(ButtonBuilder.DefaultModes, ButtonBuilder.ListFrom(null, "mode_list", ButtonBuilder.DefaultModes));
        var empty = new Dictionary<string, string> { ["ant_list"] = "" };
        Assert.Equal(ButtonBuilder.DefaultAntennas, ButtonBuilder.ListFrom(empty, "ant_list", ButtonBuilder.DefaultAntennas));
    }

    [Theory]
    [InlineData("USB", 100, 2900)]
    [InlineData("lsb", -2900, -100)]
    [InlineData("CW", 350, 850)]
    [InlineData("FM", -8000, 8000)]
    [InlineData("DIGL", -2900, -100)]
    [InlineData("UNKNOWN", 100, 2900)]
    public void DefaultFilter_per_mode(string mode, int lo, int hi) =>
        Assert.Equal((lo, hi), ButtonBuilder.DefaultFilter(mode));

    [Fact]
    public void Build_produces_the_standard_four_lines_and_a_label()
    {
        var (label, lines) = ButtonBuilder.Build("144.2", "usb", "XVTA", "XVTA", 150, 2900);
        Assert.Equal("144.200 USB", label);
        Assert.Equal(new[]
        {
            "slice tune {slice} 144.200000",
            "slice set {slice} mode=USB",
            "slice set {slice} rxant=XVTA txant=XVTA",
            "filt {slice} 150 2900",
        }, lines);
    }

    [Fact]
    public void Build_adds_step_only_when_asked()
    {
        var (_, lines) = ButtonBuilder.Build("14.250", "USB", "ANT1", "ANT1", 100, 2900, step: 1000);
        Assert.Equal("slice set {slice} step=1000", lines[^1]);
        Assert.Equal(5, lines.Count);
    }

    [Theory]
    [InlineData("abc", "USB", "ANT1", "ANT1", 100, 2900)]
    [InlineData("14.25", "", "ANT1", "ANT1", 100, 2900)]
    [InlineData("14.25", "USB", "", "ANT1", 100, 2900)]
    [InlineData("14.25", "USB", "ANT1", "ANT1", 2900, 100)]
    public void Build_rejects_bad_choices(string f, string m, string rx, string tx, int lo, int hi) =>
        Assert.Throws<SequenceException>(() => ButtonBuilder.Build(f, m, rx, tx, lo, hi));
}
