// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>
/// The list moves behind drag-and-drop on the button grid. The grid shows the config's button list
/// in order (filtered by row), so rearranging is a move within that one list.
/// </summary>
public static class ListMoves
{
    /// <summary>
    /// Move <paramref name="item"/> so it takes <paramref name="target"/>'s index: dragged forward
    /// it lands just after the target, dragged back just before it, either way at the position the
    /// target held. Items in between shift by one.
    /// </summary>
    /// <returns>False when nothing moved (item or target missing, or the same).</returns>
    public static bool MoveOnto<T>(IList<T> list, T item, T target)
    {
        var from = list.IndexOf(item);
        var to = list.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return false;
        list.RemoveAt(from);
        list.Insert(to, item);
        return true;
    }

    /// <summary>Move <paramref name="item"/> to the end of the list.</summary>
    /// <returns>False when it is missing or already last.</returns>
    public static bool MoveToEnd<T>(IList<T> list, T item)
    {
        var from = list.IndexOf(item);
        if (from < 0 || from == list.Count - 1) return false;
        list.RemoveAt(from);
        list.Add(item);
        return true;
    }
}
