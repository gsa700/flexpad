using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class CommandSequenceTests
{
    private static SliceTable Table()
    {
        var t = new SliceTable();
        t.Merge("slice 0 in_use=1 active=1 tx=0 index_letter=A");
        t.Merge("slice 1 in_use=1 active=0 tx=1 index_letter=B");
        return t;
    }

    [Theory]
    [InlineData("", LineType.Skip)]
    [InlineData("   ", LineType.Skip)]
    [InlineData("# a comment", LineType.Skip)]
    [InlineData("wait 0.5", LineType.Wait)]
    [InlineData("WAIT 2", LineType.Wait)]
    [InlineData("slice tune {slice} 14.25", LineType.Command)]
    public void Classify(string line, LineType expected) => Assert.Equal(expected, CommandSequence.Classify(line).Type);

    [Fact]
    public void Wait_seconds_are_parsed_invariant()
    {
        Assert.Equal(0.5, CommandSequence.Classify("wait 0.5").WaitSeconds);
        Assert.Throws<SequenceException>(() => CommandSequence.Classify("wait soon"));
        Assert.Throws<SequenceException>(() => CommandSequence.Classify("wait -1"));
    }

    [Fact]
    public void Substitute_fills_every_placeholder()
    {
        var t = Table();
        Assert.Equal("slice tune 0 144.2", CommandSequence.Substitute("slice tune {slice} 144.2", t));
        Assert.Equal("filt 1 1 2", CommandSequence.Substitute("filt {tx} 1 2", t));
        Assert.Equal("slice set 1 tx=1", CommandSequence.Substitute("slice set {B} tx=1", t));
        Assert.Equal("ant list", CommandSequence.Substitute("ant list", t));
    }

    [Fact]
    public void Substitute_names_what_is_missing()
    {
        var ex = Assert.Throws<SequenceException>(() => CommandSequence.Substitute("slice set {C} tx=1", Table()));
        Assert.Contains("no slice C", ex.Message);
        var empty = new SliceTable();
        Assert.Contains("no active slice", Assert.Throws<SequenceException>(() => CommandSequence.Substitute("x {slice}", empty)).Message);
        Assert.Contains("no transmit slice", Assert.Throws<SequenceException>(() => CommandSequence.Substitute("x {tx}", empty)).Message);
    }

    [Fact]
    public void Run_stops_at_the_first_error_by_default()
    {
        var sent = new List<string>();
        var reports = new List<string>();
        var ok = CommandSequence.Run(
            new[] { "# c", "", "slice tune {slice} 144.2", "filt {tx} 1 2", "slice set {B} tx=1", "wait 0.01", "bogus", "never" },
            Table(),
            cmd => { sent.Add(cmd); return cmd.Contains("bogus") ? (0x50000015, "bad") : (0, ""); },
            stopOnError: true, report: reports.Add, sleep: _ => { });
        Assert.False(ok);
        Assert.Equal(new[] { "slice tune 0 144.2", "filt 1 1 2", "slice set 1 tx=1", "bogus" }, sent);
        Assert.Single(reports);
        Assert.Contains("0x50000015", reports[0]);
    }

    [Fact]
    public void Run_continues_when_told_to()
    {
        var sent = new List<string>();
        var reports = new List<string>();
        var ok = CommandSequence.Run(
            new[] { "bogus", "slice set {C} tx=1", "filt {A} 1 2" },
            Table(),
            cmd => { sent.Add(cmd); return cmd == "bogus" ? (1, "bad") : (0, ""); },
            stopOnError: false, report: reports.Add, sleep: _ => { });
        Assert.False(ok);
        Assert.Equal(new[] { "bogus", "filt 0 1 2" }, sent);
        Assert.Equal(2, reports.Count);
    }

    [Fact]
    public void Run_waits_between_commands()
    {
        var slept = new List<double>();
        var ok = CommandSequence.Run(new[] { "a", "wait 0.5", "b" }, Table(), _ => (0, ""), true, null, slept.Add);
        Assert.True(ok);
        Assert.Equal(new[] { 0.5 }, slept);
    }

    [Fact]
    public void Run_reports_a_send_exception_as_an_error()
    {
        var reports = new List<string>();
        var ok = CommandSequence.Run(new[] { "a" }, Table(),
            _ => throw new NotConnectedException("not connected to the radio"), true, reports.Add, _ => { });
        Assert.False(ok);
        Assert.Contains("not connected", reports[0]);
    }
}
