// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

using System.Text;
using System.Text.RegularExpressions;

namespace FlexPad.Core;

/// <summary>
/// The FlexControl USB knob's wire protocol, verified against a real unit on 2026-09-14.
/// </summary>
/// <remarks>
/// A USB serial device (vendor 0x2192, product 0x0010) at 9600 8N1. It sends semicolon-terminated
/// tokens with no line endings: <c>F0304;</c> once when a host opens it; <c>U</c>/<c>D</c> for one
/// knob tick clockwise/counter-clockwise, <c>U03</c> when three ticks arrived in one USB poll;
/// <c>S</c>/<c>L</c>/<c>C</c> for short press, hold and double click of the knob; <c>X1S</c>..
/// <c>X3C</c> the same for the three aux buttons. Clockwise is U.
/// </remarks>
public static partial class KnobProtocol
{
    public const int VendorId = 0x2192;
    public const int ProductId = 0x0010;
    public const int BaudRate = 9600;

    public static readonly string[] Events =
        { "S", "L", "C", "X1S", "X1L", "X1C", "X2S", "X2L", "X2C", "X3S", "X3L", "X3C" };

    public static readonly IReadOnlyDictionary<string, string> EventNames = new Dictionary<string, string>
    {
        ["S"] = "Knob press", ["L"] = "Knob hold", ["C"] = "Knob double click",
        ["X1S"] = "AUX1 press", ["X1L"] = "AUX1 hold", ["X1C"] = "AUX1 double",
        ["X2S"] = "AUX2 press", ["X2L"] = "AUX2 hold", ["X2C"] = "AUX2 double",
        ["X3S"] = "AUX3 press", ["X3L"] = "AUX3 hold", ["X3C"] = "AUX3 double",
    };

    [GeneratedRegex(@"^([UD])(\d*)$")]
    private static partial Regex Tick();

    /// <summary>
    /// Interpret one batch of complete tokens: the net knob movement (positive = U) and the
    /// button events in order. Unknown tokens are returned so the caller can log them.
    /// </summary>
    public static (int Delta, List<string> Buttons, List<string> Unknown) Interpret(IEnumerable<string> tokens)
    {
        var delta = 0;
        var buttons = new List<string>();
        var unknown = new List<string>();
        foreach (var raw in tokens)
        {
            var tok = raw.Trim();
            if (tok.Length == 0 || tok.StartsWith('F')) continue;   // F0304 is the hello on open
            var m = Tick().Match(tok);
            if (m.Success)
            {
                var n = m.Groups[2].Value.Length == 0 ? 1 : int.Parse(m.Groups[2].Value);
                delta += m.Groups[1].Value == "U" ? n : -n;
            }
            else if (EventNames.ContainsKey(tok)) buttons.Add(tok);
            else unknown.Add(tok);
        }
        return (delta, buttons, unknown);
    }
}

/// <summary>Accumulates raw bytes and yields complete semicolon-terminated tokens.</summary>
public sealed class KnobTokenizer
{
    private readonly StringBuilder _buf = new();

    public List<string> Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) _buf.Append((char)b);
        var text = _buf.ToString();
        var last = text.LastIndexOf(';');
        if (last < 0) return new List<string>();
        var complete = text[..last];
        _buf.Clear();
        _buf.Append(text[(last + 1)..]);
        return complete.Split(';').ToList();
    }
}

/// <summary>Decisions the knob's built-in actions need. Pure and tested.</summary>
public static class KnobPolicy
{
    public const string ActionStep = "@step";
    public const string ActionNextSlice = "@next-slice";
    public const string ActionMute = "@mute";
    public const string ActionTx = "@tx";
    public static readonly string[] Actions = { ActionStep, ActionNextSlice, ActionMute, ActionTx };

    /// <summary>The frequency after <paramref name="deltaTicks"/> ticks of <paramref name="stepHz"/>.</summary>
    public static long TuneTarget(long currentHz, int deltaTicks, int stepHz) =>
        Math.Max(0, currentHz + (long)deltaTicks * stepHz);

    /// <summary>The next tuning step in the cycle: the first larger than the current one, else the smallest.</summary>
    public static int NextStep(IReadOnlyList<int> steps, int current)
    {
        if (steps.Count == 0) return 100;
        foreach (var s in steps) if (s > current) return s;
        return steps[0];
    }

    /// <summary>The slice after the active one in a circular list; null if fewer than two.</summary>
    public static string? NextSlice(IReadOnlyList<string> live, string? active)
    {
        if (live.Count < 2) return null;
        var i = active is null ? -1 : live.ToList().IndexOf(active);
        return live[(i + 1) % live.Count];
    }
}
