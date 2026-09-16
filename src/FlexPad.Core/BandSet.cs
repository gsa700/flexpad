// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>One band and where a button for it should land by default.</summary>
/// <param name="PhoneMhz">A usual SSB spot, or the CW/digital spot for a band with no phone segment.</param>
/// <param name="CwMhz">A usual CW spot.</param>
/// <param name="PhoneMode">LSB below 10 MHz by convention, USB above; CW where there is no phone;
/// AM for the broadcast set.</param>
/// <param name="Transverter">Which transverter port group the band belongs to: null for HF/6 m,
/// "A" for 2 m, "B" for 70 cm — the operator picks the port for each group.</param>
/// <param name="PanBand">What <c>display pan set … band=</c> takes for the band ("20"), or null when
/// the radio has no band of its own for it: transverter bands go by the radio's transverter list
/// (<c>x0</c>, <c>x1</c>), broadcast bands are general coverage and can only be tuned.</param>
public sealed record Band(string Name, double PhoneMhz, double CwMhz, string PhoneMode, string? Transverter, string? PanBand = null);

/// <summary>A generated button, before it becomes config.</summary>
public sealed record GeneratedButton(string Label, string? Key, string? Color, List<string> Lines);

/// <summary>
/// Makes a set of band buttons in one go: amateur bands as a band change the radio completes from
/// its per-band memory, or as a full recipe from the same lines Build-from-choices produces; and a
/// broadcast set (AM broadcast, the shortwave broadcast bands, 11 m CB, WWV) as full recipes in AM.
/// </summary>
public static class BandSet
{
    public static readonly Band[] Bands =
    {
        new("160m", 1.900, 1.830, "LSB", null, "160"),
        new("80m", 3.850, 3.550, "LSB", null, "80"),
        new("40m", 7.200, 7.030, "LSB", null, "40"),
        new("30m", 10.120, 10.120, "CW", null, "30"),      // no phone segment
        new("20m", 14.250, 14.030, "USB", null, "20"),
        new("17m", 18.130, 18.080, "USB", null, "17"),
        new("15m", 21.300, 21.030, "USB", null, "15"),
        new("12m", 24.950, 24.900, "USB", null, "12"),
        new("10m", 28.400, 28.030, "USB", null, "10"),
        new("6m", 50.125, 50.090, "USB", null, "6"),
        new("2m", 144.200, 144.050, "USB", "A"),
        new("70cm", 432.100, 432.050, "USB", "B"),
    };

    /// <summary>
    /// General coverage, so no band command exists for these (the radio refuses <c>band=gen</c>);
    /// each is a retune to a spot inside the band, in AM, on the HF antenna. Order is by frequency.
    /// </summary>
    public static readonly Band[] Broadcast =
    {
        new("AM BC", 1.000, 1.000, "AM", null),      // 530–1700 kHz
        new("120m BC", 2.400, 2.400, "AM", null),    // 2300–2495
        new("90m BC", 3.300, 3.300, "AM", null),     // 3200–3400
        new("75m BC", 3.950, 3.950, "AM", null),     // 3900–4000
        new("60m BC", 4.900, 4.900, "AM", null),     // 4750–5060
        new("49m BC", 6.000, 6.000, "AM", null),     // 5900–6200
        new("41m BC", 7.300, 7.300, "AM", null),     // 7200–7450
        new("31m BC", 9.600, 9.600, "AM", null),     // 9400–9900
        new("WWV", 10.000, 10.000, "AM", null),      // also 2.5, 5, 15, 20
        new("25m BC", 11.800, 11.800, "AM", null),   // 11600–12100
        new("22m BC", 13.700, 13.700, "AM", null),   // 13570–13870
        new("19m BC", 15.400, 15.400, "AM", null),   // 15100–15800
        new("16m BC", 17.700, 17.700, "AM", null),   // 17480–17900
        new("15m BC", 18.950, 18.950, "AM", null),   // 18900–19020
        new("13m BC", 21.600, 21.600, "AM", null),   // 21450–21850
        new("11m BC", 25.900, 25.900, "AM", null),   // 25670–26100
        new("CB", 27.185, 27.185, "AM", null),       // channel 19
    };

    public const string TransverterColor = "#2d6a4f";
    public const string BroadcastColor = "#6b4f2a";

    /// <summary>Full recipe per amateur band: tune, mode, both antenna ports, filter.</summary>
    /// <param name="selected">Band names to generate, in table order regardless of input order.</param>
    /// <param name="hfAntenna">Port for HF and 6 m.</param>
    /// <param name="xvtAAntenna">Port for the 2 m transverter.</param>
    /// <param name="xvtBAntenna">Port for the 70 cm transverter.</param>
    /// <param name="cw">CW spots and mode instead of phone.</param>
    /// <param name="hotkeys">Assign F1.. in order (F12 at most).</param>
    public static List<GeneratedButton> Generate(IEnumerable<string> selected, string hfAntenna,
        string xvtAAntenna, string xvtBAntenna, bool cw, bool hotkeys)
    {
        var wanted = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<GeneratedButton>();
        var fkey = 1;
        foreach (var band in Bands)
        {
            if (!wanted.Contains(band.Name)) continue;
            var mode = cw ? "CW" : band.PhoneMode;
            var mhz = cw ? band.CwMhz : band.PhoneMhz;
            var ant = band.Transverter switch { "A" => xvtAAntenna, "B" => xvtBAntenna, _ => hfAntenna };
            var (lo, hi) = ButtonBuilder.DefaultFilter(mode);
            var (_, lines) = ButtonBuilder.Build(
                mhz.ToString(System.Globalization.CultureInfo.InvariantCulture), mode, ant, ant, lo, hi);
            var key = hotkeys && fkey <= 12 ? $"F{fkey++}" : null;
            var label = cw ? $"{band.Name} CW" : band.Name;
            result.Add(new GeneratedButton(label, key, band.Transverter is null ? null : TransverterColor, lines));
        }
        return result;
    }

    /// <summary>
    /// One line per amateur band, <c>display pan set {pan} band=20</c>: the radio brings back the
    /// frequency, mode, filter and antenna ports it last had on that band (band persistence,
    /// verified on a FLEX-8600M 2026-09-16). Transverter bands take the radio's index for the band
    /// (<c>band=x0</c>), looked up by name in <paramref name="xvtrIndexByName"/>; a band the radio has
    /// no transverter entry for is returned in Skipped rather than made into a button that fails.
    /// </summary>
    public static (List<GeneratedButton> Buttons, List<string> Skipped) GenerateBandChange(
        IEnumerable<string> selected, IReadOnlyDictionary<string, string> xvtrIndexByName, bool hotkeys)
    {
        var wanted = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<GeneratedButton>();
        var skipped = new List<string>();
        var fkey = 1;
        foreach (var band in Bands)
        {
            if (!wanted.Contains(band.Name)) continue;
            var code = band.PanBand;
            if (code is null && band.Transverter is not null && TryXvtr(xvtrIndexByName, band.Name, out var index))
                code = "x" + index;
            if (code is null) { skipped.Add(band.Name); continue; }
            var key = hotkeys && fkey <= 12 ? $"F{fkey++}" : null;
            result.Add(new GeneratedButton(band.Name, key, band.Transverter is null ? null : TransverterColor,
                new List<string> { $"display pan set {{pan}} band={code}" }));
        }
        return (result, skipped);
    }

    /// <summary>Full recipe per broadcast band: tune, AM, the HF antenna on both ports, AM filter.</summary>
    public static List<GeneratedButton> GenerateBroadcast(IEnumerable<string> selected, string antenna, bool hotkeys)
    {
        var wanted = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<GeneratedButton>();
        var fkey = 1;
        foreach (var band in Broadcast)
        {
            if (!wanted.Contains(band.Name)) continue;
            var (lo, hi) = ButtonBuilder.DefaultFilter(band.PhoneMode);
            var (_, lines) = ButtonBuilder.Build(
                band.PhoneMhz.ToString(System.Globalization.CultureInfo.InvariantCulture), band.PhoneMode, antenna, antenna, lo, hi);
            var key = hotkeys && fkey <= 12 ? $"F{fkey++}" : null;
            result.Add(new GeneratedButton(band.Name, key, BroadcastColor, lines));
        }
        return result;
    }

    private static bool TryXvtr(IReadOnlyDictionary<string, string> byName, string name, out string index)
    {
        if (byName.TryGetValue(name, out index!)) return true;
        foreach (var (k, v) in byName)
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) { index = v; return true; }
        index = "";
        return false;
    }
}
