// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>
/// Turn the radio's current state into the command lines that recreate it — what a memory channel
/// would store, plus the antenna ports SmartSDR's memories leave out. One slice (the active one,
/// addressed as <c>{slice}</c> so the button works on whichever slice is active later) or all of
/// them (addressed by letter, behind a <c>slices A B</c> line that opens and closes slices to match).
/// </summary>
public static class SliceCapture
{
    /// <param name="full">Add tuning step, AGC, noise tools, RF gain, DAX, TX power and the scope's
    /// width and centre.</param>
    /// <param name="pans">Panadapters by handle, from the radio client; null or a missing handle just
    /// leaves the scope lines out.</param>
    /// <returns>A suggested button label and the lines.</returns>
    public static (string Label, List<string> Lines) Capture(SliceTable slices,
        IReadOnlyDictionary<string, string> transmit, bool full, DateTime? now = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? pans = null)
    {
        var idx = slices.Active() ?? throw new SequenceException("no active slice to capture");
        var s = slices.Get(idx)!;
        var stamp = (now ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm");
        var lines = new List<string> { $"# captured from slice {s.GetValueOrDefault("index_letter", "?")} on {stamp}" };
        lines.AddRange(SliceLines(s, "{slice}", full));
        if (full)
        {
            lines.AddRange(PowerLines(transmit));
            lines.AddRange(ScopeLines(s, "{pan}", pans));
        }
        return (LabelOf(s), lines);
    }

    /// <summary>
    /// Every open slice: a <c>slices</c> line naming the letters, each slice's settings by letter,
    /// then which slice transmits and which is active. Self-contained: nothing is stored in the radio.
    /// </summary>
    public static (string Label, List<string> Lines) CaptureAll(SliceTable slices,
        IReadOnlyDictionary<string, string> transmit, bool full, DateTime? now = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? pans = null)
    {
        var live = slices.Live()
            .Select(i => slices.Get(i)!)
            .Where(s => s.GetValueOrDefault("index_letter", "").Length > 0)
            .OrderBy(s => s["index_letter"], StringComparer.Ordinal)
            .ToList();
        if (live.Count == 0) throw new SequenceException("no open slices to capture");

        var letters = live.Select(s => s["index_letter"]).ToList();
        var stamp = (now ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm");
        var lines = new List<string>
        {
            $"# captured {live.Count} slice{(live.Count == 1 ? "" : "s")} ({string.Join(", ", letters)}) on {stamp}",
            "# opens or closes slices until exactly these exist, then sets each one up",
            $"slices {string.Join(' ', letters)}",
        };
        foreach (var s in live)
        {
            lines.Add($"# slice {s["index_letter"]}");
            lines.AddRange(SliceLines(s, "{" + s["index_letter"] + "}", full));
        }
        var tx = live.FirstOrDefault(s => s.GetValueOrDefault("tx") == "1");
        if (tx is not null) lines.Add($"slice set {{{tx["index_letter"]}}} tx=1");
        var active = live.FirstOrDefault(s => s.GetValueOrDefault("active") == "1");
        if (active is not null) lines.Add($"slice set {{{active["index_letter"]}}} active=1");
        if (full)
        {
            lines.AddRange(PowerLines(transmit));
            // One pair of scope lines per panadapter, addressed through the first slice that lives in it.
            foreach (var s in live.GroupBy(x => x.GetValueOrDefault("pan", "")).Where(g => g.Key.Length > 0).Select(g => g.First()))
                lines.AddRange(ScopeLines(s, "{pan" + s["index_letter"] + "}", pans));
        }

        var label = LabelOf(active ?? live[0]);
        if (live.Count > 1) label += $" +{live.Count - 1}";
        return (label, lines);
    }

    private static IEnumerable<string> SliceLines(IReadOnlyDictionary<string, string> s, string who, bool full)
    {
        string G(string key, string fallback) => s.TryGetValue(key, out var v) ? v : fallback;
        yield return $"slice tune {who} {G("RF_frequency", "?")}";
        yield return $"slice set {who} mode={G("mode", "?")}";
        yield return $"slice set {who} rxant={G("rxant", "ANT1")} txant={G("txant", "ANT1")}";
        yield return $"filt {who} {G("filter_lo", "100")} {G("filter_hi", "2900")}";
        if (!full) yield break;
        yield return $"slice set {who} step={G("step", "100")}";
        yield return $"slice set {who} agc_mode={G("agc_mode", "med")} agc_threshold={G("agc_threshold", "60")}";
        yield return $"slice set {who} nr={G("nr", "0")} nr_level={G("nr_level", "50")}";
        yield return $"slice set {who} nb={G("nb", "0")} nb_level={G("nb_level", "50")}";
        yield return $"slice set {who} wnb={G("wnb", "0")} wnb_level={G("wnb_level", "50")}";
        yield return $"slice set {who} anf={G("anf", "0")}";
        yield return $"slice set {who} rfgain={G("rfgain", "0")}";
        yield return $"slice set {who} dax={G("dax", "0")}";
    }

    /// <summary>
    /// The scope: width first, then centre. Last in a captured button on purpose: a slice the
    /// button had to open arrives with a very wide default scope (David, 2026-09-19), and by then
    /// everything that matters more has already been applied.
    /// </summary>
    private static IEnumerable<string> ScopeLines(IReadOnlyDictionary<string, string> slice, string who,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? pans)
    {
        if (pans is null || !slice.TryGetValue("pan", out var handle) || !pans.TryGetValue(handle, out var pan)) yield break;
        if (pan.TryGetValue("bandwidth", out var bw) && bw.Length > 0) yield return $"display pan set {who} bandwidth={bw}";
        if (pan.TryGetValue("center", out var c) && c.Length > 0) yield return $"display pan set {who} center={c}";
    }

    private static IEnumerable<string> PowerLines(IReadOnlyDictionary<string, string> transmit)
    {
        if (transmit.TryGetValue("rfpower", out var rf)) yield return $"transmit set rfpower={rf}";
        if (transmit.TryGetValue("tunepower", out var tp)) yield return $"transmit set tunepower={tp}";
    }

    private static string LabelOf(IReadOnlyDictionary<string, string> s)
    {
        var mode = s.GetValueOrDefault("mode", "?");
        return double.TryParse(s.GetValueOrDefault("RF_frequency", "?"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? $"{f.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} {mode}"
            : mode;
    }
}
