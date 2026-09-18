using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

/// <summary>
/// A stand-in radio for the <c>slices</c> line: it keeps a slice table the way the real one does
/// (a new slice gets the lowest free index and the lowest free letter, a removed one goes
/// <c>in_use=0</c>) and logs every command it was sent.
/// </summary>
internal sealed class FakeRadio
{
    public SliceTable Slices { get; } = new();
    public List<string> Sent { get; } = new();
    public int MaxSlices { get; init; } = 4;

    public FakeRadio(params string[] letters)
    {
        foreach (var l in letters) Open(l, "14.100000", "ANT1", "USB");
        var first = Slices.Live().FirstOrDefault();
        if (first is not null) Slices.Merge($"slice {first} active=1 tx=1");
    }

    private string Open(string letter, string freq, string ant, string mode)
    {
        var used = Slices.Live().ToHashSet();
        var idx = Enumerable.Range(0, 8).Select(i => i.ToString()).First(i => !used.Contains(i));
        Slices.Merge($"slice {idx} in_use=1 index_letter={letter} RF_frequency={freq} rxant={ant} txant={ant} mode={mode} active=0 tx=0");
        return idx;
    }

    public (int Code, string Text) Send(string cmd)
    {
        Sent.Add(cmd);
        if (cmd.StartsWith("slice remove "))
        {
            Slices.Merge($"slice {cmd[13..]} in_use=0 active=0 tx=0");
            return (0, "");
        }
        if (cmd.StartsWith("slice create "))
        {
            if (Slices.Live().Count >= MaxSlices) return (0x50000003, "no more slices");
            var kv = cmd[13..].Split(' ').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
            var have = Slices.Live().Select(i => Slices.Get(i)!["index_letter"]).ToHashSet();
            var letter = "ABCDEFGH".Select(c => c.ToString()).First(l => !have.Contains(l));
            return (0, Open(letter, kv["freq"], kv["ant"], kv["mode"]));
        }
        return (0, "");
    }

    public string Letters() => string.Join("", Slices.Live().Select(i => Slices.Get(i)!["index_letter"]).OrderBy(l => l));
}

public class SlicesLineTests
{
    private static bool Run(FakeRadio r, List<string> errors, params string[] lines) =>
        CommandSequence.Run(lines, r.Slices, r.Send, stopOnError: true, errors.Add, _ => { });

    [Fact]
    public void Classifies_letters_in_any_case_and_order_and_rejects_nonsense()
    {
        Assert.Equal(new SequenceLine(LineType.Slices, "A B", 0), CommandSequence.Classify("slices b a"));
        Assert.Equal(new SequenceLine(LineType.Slices, "A", 0), CommandSequence.Classify("  SLICES A A "));
        Assert.Throws<SequenceException>(() => CommandSequence.Classify("slices"));
        Assert.Throws<SequenceException>(() => CommandSequence.Classify("slices 2"));
        Assert.Throws<SequenceException>(() => CommandSequence.Classify("slices A Z"));
        // the radio's own commands are untouched
        Assert.Equal(LineType.Command, CommandSequence.Classify("slice set {A} tx=1").Type);
    }

    [Fact]
    public void Opens_a_missing_slice_as_a_copy_of_the_active_one_then_the_letter_resolves()
    {
        var r = new FakeRadio("A");
        r.Slices.Merge("slice 0 RF_frequency=3.925000 rxant=ANT2 mode=LSB");
        var errors = new List<string>();
        Assert.True(Run(r, errors, "slices A B", "slice tune {B} 14.250"));
        Assert.Empty(errors);
        Assert.Equal("AB", r.Letters());
        Assert.Equal(new[] { "slice create freq=3.925000 ant=ANT2 mode=LSB", "slice tune 1 14.250" }, r.Sent);
    }

    [Fact]
    public void Closes_slices_that_are_not_wanted_and_does_nothing_when_it_already_matches()
    {
        var r = new FakeRadio("A", "B", "C");
        var errors = new List<string>();
        Assert.True(Run(r, errors, "slices A"));
        Assert.Equal("A", r.Letters());
        Assert.Equal(new[] { "slice remove 1", "slice remove 2" }, r.Sent);

        r.Sent.Clear();
        Assert.True(Run(r, errors, "slices A"));
        Assert.Empty(r.Sent);
    }

    [Fact]
    public void A_wanted_letter_above_a_gap_is_reached_with_a_filler_that_is_closed_again()
    {
        var r = new FakeRadio("A");
        var errors = new List<string>();
        Assert.True(Run(r, errors, "slices A C"));
        Assert.Empty(errors);
        Assert.Equal("AC", r.Letters());
        Assert.Equal(2, r.Sent.Count(c => c.StartsWith("slice create")));
        Assert.Equal("slice remove 1", r.Sent.Last());   // the filler B
    }

    [Fact]
    public void From_nothing_open_it_uses_a_plain_default_slice()
    {
        var r = new FakeRadio();
        Assert.True(Run(r, new List<string>(), "slices A"));
        Assert.Equal("slice create freq=14.100000 ant=ANT1 mode=USB", r.Sent[0]);
        Assert.Equal("A", r.Letters());
    }

    [Fact]
    public void A_refused_create_stops_the_button_with_the_radios_reason()
    {
        var r = new FakeRadio("A", "B") { MaxSlices = 2 };
        var errors = new List<string>();
        Assert.False(Run(r, errors, "slices A B C", "slice tune {C} 7.1"));
        Assert.Contains(errors, e => e.Contains("no more slices"));
        Assert.DoesNotContain(r.Sent, c => c.StartsWith("slice tune"));
    }
}
