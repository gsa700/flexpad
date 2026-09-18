using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class ButtonActionsTests
{
    private static ButtonAction Named(string name) => ButtonActions.All.Single(a => a.Name == name);

    [Fact]
    public void Every_action_makes_a_button_from_its_own_default()
    {
        foreach (var a in ButtonActions.All)
        {
            var (label, lines) = a.Make(a.DefaultValue);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.Equal(LineType.Command, CommandSequence.Classify(l).Type));
        }
    }

    [Fact]
    public void Antenna_sets_both_ports_on_the_active_slice()
    {
        var (label, lines) = Named("Switch antenna").Make(" XVTB ");
        Assert.Equal("XVTB", label);
        Assert.Equal(new[] { "slice set {slice} rxant=XVTB txant=XVTB" }, lines);
    }

    [Fact]
    public void Slice_actions_address_the_slice_by_letter_in_any_case()
    {
        Same("TX -> B", "slice set {B} tx=1", Named("Move TX to a slice").Make("b"));
        Same("Slice C", "slice set {C} active=1", Named("Make a slice active").Make("C"));
        Same("Close B", "slice remove {B}", Named("Close a slice").Make("B"));
    }

    [Fact]
    public void Power_is_a_whole_number_in_range()
    {
        Same("Power 75", "transmit set rfpower=75", Named("Set TX power").Make("75"));
        Assert.Throws<SequenceException>(() => Named("Set TX power").Make("101"));
        Assert.Throws<SequenceException>(() => Named("Set TX power").Make("lots"));
    }

    [Fact]
    public void Bad_input_is_a_message_not_a_broken_button()
    {
        Assert.Throws<SequenceException>(() => Named("Switch antenna").Make(" "));
        Assert.Throws<SequenceException>(() => Named("Close a slice").Make("Z"));
    }

    private static void Same(string label, string onlyLine, (string Label, List<string> Lines) made)
    {
        Assert.Equal(label, made.Label);
        Assert.Equal(new[] { onlyLine }, made.Lines);
    }
}
