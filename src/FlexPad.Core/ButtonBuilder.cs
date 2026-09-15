// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>
/// The editor's "build from choices" helper: turns a frequency, mode, two antenna ports, a filter
/// and an optional step into the command lines a button needs. The choice lists come from the
/// radio's own slice status (<c>mode_list</c>, <c>ant_list</c>, <c>tx_ant_list</c>), so a 6600 with
/// one transverter port and an 8600 with two each offer what they actually have.
/// </summary>
public static class ButtonBuilder
{
    /// <summary>Fallbacks for when no slice status has arrived yet.</summary>
    public static readonly string[] DefaultModes = { "USB", "LSB", "CW", "AM", "SAM", "FM", "NFM", "DFM", "DIGU", "DIGL", "RTTY" };
    public static readonly string[] DefaultAntennas = { "ANT1", "ANT2", "XVTA", "XVTB" };
    public static readonly int[] StepChoices = { 1, 10, 50, 100, 500, 1000, 5000, 10000 };

    /// <summary>A comma-separated status list ("USB,LSB,CW") as items; the fallback when absent.</summary>
    public static List<string> ListFrom(IReadOnlyDictionary<string, string>? slice, string key, IEnumerable<string> fallback)
    {
        if (slice is not null && slice.TryGetValue(key, out var raw) && raw.Length > 0)
        {
            var items = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (items.Count > 0) return items;
        }
        return fallback.ToList();
    }

    /// <summary>The filter edges SmartSDR would typically use for a mode, as (low, high) in Hz.</summary>
    public static (int Low, int High) DefaultFilter(string mode) => mode.ToUpperInvariant() switch
    {
        "LSB" or "DIGL" => (-2900, -100),
        "USB" or "DIGU" => (100, 2900),
        "CW" => (350, 850),
        "RTTY" => (-2300, -1700),
        "AM" or "SAM" => (-3000, 3000),
        "FM" or "NFM" or "DFM" => (-8000, 8000),
        _ => (100, 2900),
    };

    /// <summary>
    /// The command lines for the choices, and a label suggestion. Frequency is validated; the rest
    /// is passed through as chosen. Step 0 or negative means "leave the step alone".
    /// </summary>
    public static (string Label, List<string> Lines) Build(string frequencyMhz, string mode,
        string rxAnt, string txAnt, int filterLow, int filterHigh, int step = 0)
    {
        if (!double.TryParse(frequencyMhz.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mhz) || mhz <= 0)
            throw new SequenceException($"'{frequencyMhz}' is not a frequency in MHz");
        if (string.IsNullOrWhiteSpace(mode)) throw new SequenceException("choose a mode");
        if (string.IsNullOrWhiteSpace(rxAnt) || string.IsNullOrWhiteSpace(txAnt))
            throw new SequenceException("choose both antenna ports");
        if (filterLow >= filterHigh) throw new SequenceException("filter low must be below filter high");

        var freq = FlexProtocol.FormatMhz((long)Math.Round(mhz * 1_000_000));
        var lines = new List<string>
        {
            $"slice tune {{slice}} {freq}",
            $"slice set {{slice}} mode={mode.Trim().ToUpperInvariant()}",
            $"slice set {{slice}} rxant={rxAnt.Trim()} txant={txAnt.Trim()}",
            $"filt {{slice}} {filterLow} {filterHigh}",
        };
        if (step > 0) lines.Add($"slice set {{slice}} step={step}");
        var label = $"{mhz.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} {mode.Trim().ToUpperInvariant()}";
        return (label, lines);
    }
}
