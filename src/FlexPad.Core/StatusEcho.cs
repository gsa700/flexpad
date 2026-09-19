// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

using System.Text.RegularExpressions;

namespace FlexPad.Core;

/// <summary>
/// The status the radio never sends us: our own changes.
/// </summary>
/// <remarks>
/// Seen live on a FLEX-8600M, 2026-09-19, with two connections: a <c>slice set … agc_threshold=</c>,
/// <c>audio_level=</c> or <c>display pan set … average=</c> from client X is pushed to every OTHER
/// client as a status line, is applied (a fresh connection reads the new value), and is never echoed
/// to X; re-subscribing does not re-dump either. So a client that changes a setting and later reads
/// its own table sees the old value. FlexPad did exactly that: a button set AGC-T, and the next
/// capture wrote the stale AGC-T into the new preset (David: "didn't preserve the slice B volume or
/// AGC-T settings"). When the radio accepts one of these commands we therefore apply it to our own
/// tables, as the status line the radio would have sent anyone else.
/// </remarks>
public static partial class StatusEcho
{
    [GeneratedRegex(@"^slice\s+s(?:et)?\s+(\d+)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SliceSet();

    [GeneratedRegex(@"^display\s+pan\s+s(?:et)?\s+(0x[0-9A-Fa-f]+)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PanSet();

    [GeneratedRegex(@"^transmit\s+s(?:et)?\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TransmitSet();

    [GeneratedRegex(@"^filt\s+(\d+)\s+(-?\d+)\s+(-?\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Filt();

    // The radio does report these itself, and they move other slices too (one active, one TX):
    // leave them to the real status lines. `band` makes the radio retune from its own memory.
    private static readonly HashSet<string> NotOurs = new(StringComparer.Ordinal) { "active", "tx", "band" };

    /// <summary>
    /// The status body an accepted command amounts to (<c>slice 1 agc_threshold=52</c>,
    /// <c>display pan 0x40000001 average=69</c>, <c>transmit rfpower=33</c>), or null when the
    /// command is not a plain setting.
    /// </summary>
    public static string? For(string command)
    {
        var cmd = command.Trim();
        if (SliceSet().Match(cmd) is { Success: true } s) return Body($"slice {s.Groups[1].Value}", s.Groups[2].Value);
        if (PanSet().Match(cmd) is { Success: true } p) return Body($"display pan {p.Groups[1].Value}", p.Groups[2].Value);
        if (TransmitSet().Match(cmd) is { Success: true } t) return Body("transmit", t.Groups[1].Value);
        if (Filt().Match(cmd) is { Success: true } f)
            return $"slice {f.Groups[1].Value} filter_lo={f.Groups[2].Value} filter_hi={f.Groups[3].Value}";
        return null;
    }

    private static string? Body(string head, string pairs)
    {
        var kept = FlexProtocol.KeyValues(pairs).Where(kv => !NotOurs.Contains(kv.Key)).Select(kv => $"{kv.Key}={kv.Value}").ToList();
        return kept.Count == 0 ? null : $"{head} {string.Join(' ', kept)}";
    }
}
