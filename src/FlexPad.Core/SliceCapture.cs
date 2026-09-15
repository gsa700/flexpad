// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>
/// Turn the active slice's current state into the command lines that recreate it — what a
/// memory channel would store, plus the antenna ports SmartSDR's memories leave out.
/// </summary>
public static class SliceCapture
{
    /// <param name="full">Add tuning step, AGC, noise tools, RF gain, DAX and TX power.</param>
    /// <returns>A suggested button label and the lines.</returns>
    public static (string Label, List<string> Lines) Capture(SliceTable slices,
        IReadOnlyDictionary<string, string> transmit, bool full, DateTime? now = null)
    {
        var idx = slices.Active() ?? throw new SequenceException("no active slice to capture");
        var s = slices.Get(idx)!;
        string G(string key, string fallback) => s.TryGetValue(key, out var v) ? v : fallback;

        var freq = G("RF_frequency", "?");
        var mode = G("mode", "?");
        var stamp = (now ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm");
        var lines = new List<string>
        {
            $"# captured from slice {G("index_letter", "?")} on {stamp}",
            $"slice tune {{slice}} {freq}",
            $"slice set {{slice}} mode={mode}",
            $"slice set {{slice}} rxant={G("rxant", "ANT1")} txant={G("txant", "ANT1")}",
            $"filt {{slice}} {G("filter_lo", "100")} {G("filter_hi", "2900")}",
        };
        if (full)
        {
            lines.Add($"slice set {{slice}} step={G("step", "100")}");
            lines.Add($"slice set {{slice}} agc_mode={G("agc_mode", "med")} agc_threshold={G("agc_threshold", "60")}");
            lines.Add($"slice set {{slice}} nr={G("nr", "0")} nr_level={G("nr_level", "50")}");
            lines.Add($"slice set {{slice}} nb={G("nb", "0")} nb_level={G("nb_level", "50")}");
            lines.Add($"slice set {{slice}} wnb={G("wnb", "0")} wnb_level={G("wnb_level", "50")}");
            lines.Add($"slice set {{slice}} anf={G("anf", "0")}");
            lines.Add($"slice set {{slice}} rfgain={G("rfgain", "0")}");
            lines.Add($"slice set {{slice}} dax={G("dax", "0")}");
            if (transmit.TryGetValue("rfpower", out var rf)) lines.Add($"transmit set rfpower={rf}");
            if (transmit.TryGetValue("tunepower", out var tp)) lines.Add($"transmit set tunepower={tp}");
        }

        var label = double.TryParse(freq, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? $"{f.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} {mode}"
            : mode;
        return (label, lines);
    }
}
