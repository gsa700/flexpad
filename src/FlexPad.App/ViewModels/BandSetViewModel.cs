using System.Collections.ObjectModel;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.ViewModels;

/// <summary>Pick bands, ports and mode; the App turns the result into buttons.</summary>
public sealed class BandSetViewModel : ViewModelBase
{
    public BandSetViewModel(RadioService radio)
    {
        var slices = radio.Client.Slices;
        var idx = slices.Active();
        var s = idx is null ? null : slices.Get(idx);
        Antennas = ButtonBuilder.ListFrom(s, "tx_ant_list", ButtonBuilder.DefaultAntennas);
        HfAntenna = Antennas.Contains("ANT1") ? "ANT1" : Antennas.FirstOrDefault();
        XvtAAntenna = Antennas.Contains("XVTA") ? "XVTA" : Antennas.FirstOrDefault();
        XvtBAntenna = Antennas.Contains("XVTB") ? "XVTB" : Antennas.FirstOrDefault();
        foreach (var b in BandSet.Bands)
            Bands.Add(new BandRow(b, selected: b.Transverter is null));   // HF on by default
    }

    public ObservableCollection<BandRow> Bands { get; } = new();
    public List<string> Antennas { get; }

    private string? _hf;
    public string? HfAntenna { get => _hf; set => SetProperty(ref _hf, value); }

    private string? _xvtA;
    public string? XvtAAntenna { get => _xvtA; set => SetProperty(ref _xvtA, value); }

    private string? _xvtB;
    public string? XvtBAntenna { get => _xvtB; set => SetProperty(ref _xvtB, value); }

    private bool _cw;
    public bool Cw { get => _cw; set => SetProperty(ref _cw, value); }

    private bool _hotkeys;
    public bool Hotkeys { get => _hotkeys; set => SetProperty(ref _hotkeys, value); }

    private bool _bandRow = true;
    /// <summary>Put the generated buttons in the row along the bottom rather than the main grid.</summary>
    public bool BandRow { get => _bandRow; set => SetProperty(ref _bandRow, value); }

    private bool _replace = true;
    /// <summary>Overwrite a button that already has the same label rather than adding a twin.</summary>
    public bool ReplaceSameLabel { get => _replace; set => SetProperty(ref _replace, value); }

    public void SelectAll(bool on) { foreach (var b in Bands) b.IsSelected = on; }

    public List<GeneratedButton> Generate() =>
        BandSet.Generate(Bands.Where(b => b.IsSelected).Select(b => b.Band.Name),
            HfAntenna ?? "ANT1", XvtAAntenna ?? "XVTA", XvtBAntenna ?? "XVTB", Cw, Hotkeys);
}

public sealed class BandRow : ViewModelBase
{
    public BandRow(Band band, bool selected) { Band = band; _selected = selected; }
    public Band Band { get; }
    public string Name => Band.Name;
    public string Detail => Band.Transverter is null
        ? $"{Band.PhoneMhz:0.000} {Band.PhoneMode} / {Band.CwMhz:0.000} CW"
        : $"{Band.PhoneMhz:0.000} {Band.PhoneMode} / {Band.CwMhz:0.000} CW  (transverter {Band.Transverter})";

    private bool _selected;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
}
