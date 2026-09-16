using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class ListMovesTests
{
    private static List<string> Abcde() => new() { "a", "b", "c", "d", "e" };

    [Fact]
    public void Dragging_forward_lands_at_the_targets_index_just_after_it()
    {
        var l = Abcde();
        Assert.True(ListMoves.MoveOnto(l, "b", "d"));
        Assert.Equal(new[] { "a", "c", "d", "b", "e" }, l);
        Assert.Equal(3, l.IndexOf("b"));   // where d was
    }

    [Fact]
    public void Dragging_back_lands_at_the_targets_index_just_before_it()
    {
        var l = Abcde();
        Assert.True(ListMoves.MoveOnto(l, "d", "b"));
        Assert.Equal(new[] { "a", "d", "b", "c", "e" }, l);
        Assert.Equal(1, l.IndexOf("d"));   // where b was
    }

    [Fact]
    public void Onto_itself_or_a_stranger_does_nothing()
    {
        var l = Abcde();
        Assert.False(ListMoves.MoveOnto(l, "c", "c"));
        Assert.False(ListMoves.MoveOnto(l, "c", "z"));
        Assert.False(ListMoves.MoveOnto(l, "z", "c"));
        Assert.Equal(Abcde(), l);
    }

    [Fact]
    public void To_end_moves_once_and_reports_when_already_there()
    {
        var l = Abcde();
        Assert.True(ListMoves.MoveToEnd(l, "b"));
        Assert.Equal(new[] { "a", "c", "d", "e", "b" }, l);
        Assert.False(ListMoves.MoveToEnd(l, "b"));
        Assert.False(ListMoves.MoveToEnd(l, "z"));
    }
}
