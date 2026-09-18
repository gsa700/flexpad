// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>What the one input of a common action is, so the new-button window can offer the right control.</summary>
public enum ActionParameter { None, Antenna, SliceLetter, Percent }

/// <summary>
/// One of the "common action" choices in the new-button window: a single-purpose button that
/// takes at most one input. <see cref="Make"/> turns the input into a label and the command lines.
/// </summary>
public sealed record ButtonAction(string Name, string Description, ActionParameter Parameter, string ParameterLabel,
    string DefaultValue, Func<string, (string Label, List<string> Lines)> Make);

/// <summary>The common actions, in the order they are offered. Every command here is one the 8600M accepted.</summary>
public static class ButtonActions
{
    public static readonly string[] SliceLetters = { "A", "B", "C", "D", "E", "F", "G", "H" };

    public static readonly ButtonAction[] All =
    {
        new("Switch antenna", "Both antenna ports of the active slice, nothing else.",
            ActionParameter.Antenna, "Antenna", "ANT1",
            v => (Need(v, "choose an antenna"), new() { $"slice set {{slice}} rxant={v.Trim()} txant={v.Trim()}" })),

        new("Move TX to a slice", "Make one slice the transmit slice.",
            ActionParameter.SliceLetter, "Slice", "A",
            v => ($"TX -> {Letter(v)}", new() { $"slice set {{{Letter(v)}}} tx=1" })),

        new("Make a slice active", "Point the front panel and the knob at one slice.",
            ActionParameter.SliceLetter, "Slice", "A",
            v => ($"Slice {Letter(v)}", new() { $"slice set {{{Letter(v)}}} active=1" })),

        new("Close a slice", "Remove one slice; the others stay as they are.",
            ActionParameter.SliceLetter, "Slice", "B",
            v => ($"Close {Letter(v)}", new() { $"slice remove {{{Letter(v)}}}" })),

        new("Set TX power", "RF power, 0 to 100.",
            ActionParameter.Percent, "Power", "50",
            v => ($"Power {Percent(v)}", new() { $"transmit set rfpower={Percent(v)}" })),
    };

    private static string Need(string v, string message) =>
        string.IsNullOrWhiteSpace(v) ? throw new SequenceException(message) : v.Trim();

    private static string Letter(string v)
    {
        var l = (v ?? "").Trim().ToUpperInvariant();
        return Array.IndexOf(SliceLetters, l) >= 0 ? l : throw new SequenceException("choose a slice letter, A to H");
    }

    private static int Percent(string v) =>
        int.TryParse((v ?? "").Trim(), out var n) && n is >= 0 and <= 100
            ? n : throw new SequenceException("power is a whole number from 0 to 100");
}
