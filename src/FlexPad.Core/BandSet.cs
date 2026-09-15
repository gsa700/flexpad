// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>One amateur band and where a button for it should land by default.</summary>
/// <param name="PhoneMhz">A usual SSB spot, or the CW/digital spot for a band with no phone segment.</param>
/// <param name="CwMhz">A usual CW spot.</param>
/// <param name="PhoneMode">LSB below 10 MHz by convention, USB above; CW where there is no phone.</param>
/// <param name="Transverter">Which transverter port group the band belongs to: null for HF/6 m,
/// "A" for 2 m, "B" for 70 cm — the operator picks the port for each group.</param>
public sealed record Band(string Name, double PhoneMhz, double CwMhz, string PhoneMode, string? Transverter);

/// <summary>A generated button, before it becomes config.</summary>
public sealed record GeneratedButton(string Label, string? Key, string? Color, List<string> Lines);

/// <summary>
/// Makes a set of band buttons in one go, from the same lines Build-from-choices produces.
/// </summary>
public static class BandSet
{
    public static readonly Band[] Bands =
    {
        new("160m", 1.900, 1.830, "LSB", null),
        new("80m", 3.850, 3.550, "LSB", null),
        new("40m", 7.200, 7.030, "LSB", null),
        new("30m", 10.120, 10.120, "CW", null),        // no phone segment
        new("20m", 14.250, 14.030, "USB", null),
        new("17m", 18.130, 18.080, "USB", null),
        new("15m", 21.300, 21.030, "USB", null),
        new("12m", 24.950, 24.900, "USB", null),
        new("10m", 28.400, 28.030, "USB", null),
        new("6m", 50.125, 50.090, "USB", null),
        new("2m", 144.200, 144.050, "USB", "A"),
        new("70cm", 432.100, 432.050, "USB", "B"),
    };

    public const string TransverterColor = "#2d6a4f";

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
}
