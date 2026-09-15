using System.Text;
using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class KnobProtocolTests
{
    // Token stream recorded from a real FlexControl on 2026-09-14: hello, a clockwise spin with one
    // fast tick, counter-clockwise, then every button code the unit produced.

    [Fact]
    public void Interpret_sums_ticks_and_lists_buttons_in_order()
    {
        var (delta, buttons, unknown) = KnobProtocol.Interpret(new[] { "F0304", "U", "U03", "D", "X1S", "S", "D02", "C", "X3L" });
        Assert.Equal(1, delta);            // +1 +3 -1 -2
        Assert.Equal(new[] { "X1S", "S", "C", "X3L" }, buttons);
        Assert.Empty(unknown);
    }

    [Fact]
    public void Unknown_tokens_are_reported_not_dropped()
    {
        var (_, _, unknown) = KnobProtocol.Interpret(new[] { "Q9", "U" });
        Assert.Equal(new[] { "Q9" }, unknown);
    }

    [Fact]
    public void Tokenizer_yields_only_complete_tokens_and_keeps_the_rest()
    {
        var t = new KnobTokenizer();
        Assert.Empty(t.Feed(Encoding.ASCII.GetBytes("F03")));
        Assert.Equal(new[] { "F0304", "U" }, t.Feed(Encoding.ASCII.GetBytes("04;U;X1")));
        Assert.Equal(new[] { "X1S" }, t.Feed(Encoding.ASCII.GetBytes("S;")));
    }

    [Fact]
    public void Every_documented_event_has_a_name()
    {
        foreach (var e in KnobProtocol.Events) Assert.True(KnobProtocol.EventNames.ContainsKey(e), e);
        Assert.Equal(12, KnobProtocol.Events.Length);
    }
}

public class KnobPolicyTests
{
    [Fact]
    public void TuneTarget_multiplies_ticks_by_step_and_never_goes_negative()
    {
        Assert.Equal(14_250_300, KnobPolicy.TuneTarget(14_250_000, 3, 100));
        Assert.Equal(14_249_000, KnobPolicy.TuneTarget(14_250_000, -1, 1000));
        Assert.Equal(0, KnobPolicy.TuneTarget(500, -1, 1000));
    }

    [Fact]
    public void NextStep_cycles_upward_then_wraps()
    {
        var steps = new[] { 10, 100, 1000, 10000 };
        Assert.Equal(1000, KnobPolicy.NextStep(steps, 100));
        Assert.Equal(10, KnobPolicy.NextStep(steps, 10000));
        Assert.Equal(100, KnobPolicy.NextStep(steps, 50));   // between entries: the next larger
        Assert.Equal(100, KnobPolicy.NextStep(Array.Empty<int>(), 7));
    }

    [Fact]
    public void NextSlice_is_circular_and_needs_two()
    {
        Assert.Equal("1", KnobPolicy.NextSlice(new[] { "0", "1" }, "0"));
        Assert.Equal("0", KnobPolicy.NextSlice(new[] { "0", "1" }, "1"));
        Assert.Equal("0", KnobPolicy.NextSlice(new[] { "0", "1" }, null));
        Assert.Null(KnobPolicy.NextSlice(new[] { "0" }, "0"));
    }
}
