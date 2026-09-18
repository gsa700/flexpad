using System.Collections.ObjectModel;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.ViewModels;

/// <summary>Pick a set, bands, ports and mode; the App turns the result into buttons.</summary>
public sealed class BandSetViewModel : ViewModelBase
{
    private readonly Dictionary<string, string> _xvtrs;

    public BandSetViewModel(RadioService radio)
    {
        var slices = radio.Client.Slices;
        var idx = slices.Active();
        var s = idx is null ? null : slices.Get(idx);
        Antennas = ButtonBuilder.ListFrom(s, "tx_ant_list", ButtonBuilder.DefaultAntennas);
        HfAntenna = Antennas.Contains("ANT1") ? "ANT1" : Antennas.FirstOrDefault();
        XvtAAntenna = Antennas.Contains("XVTA") ? "XVTA" : Antennas.FirstOrDefault();
        XvtBAntenna = Antennas.Contains("XVTB") ? "XVTB" : Antennas.FirstOrDefault();
        _xvtrs = radio.Client.XvtrIndexByName();
        var letter = s?.GetValueOrDefault("index_letter");
        _runsOn = string.IsNullOrEmpty(letter) ? ButtonConfig.DefaultLetter : letter;
        foreach (var b in BandSet.Bands)
            Amateur.Add(new BandRow(b, selected: b.Transverter is null));   // HF on by default
        foreach (var b in BandSet.Broadcast)
            Broadcast.Add(new BandRow(b, selected: true));
    }

    public ObservableCollection<BandRow> Amateur { get; } = new();
    public ObservableCollection<BandRow> Broadcast { get; } = new();
    public List<string> Antennas { get; }

    public string[] RunsOnChoices { get; } = ButtonActions.SliceLetters.Append(ButtonEditorViewModel.ActiveSlice).ToArray();

    private string _runsOn;
    /// <summary>Which slice the generated buttons act on; defaults to the slice that is active now.</summary>
    public string RunsOn { get => _runsOn; set => SetProperty(ref _runsOn, value ?? ButtonEditorViewModel.ActiveSlice); }
    public string? RunsOnLetter => RunsOn == ButtonEditorViewModel.ActiveSlice ? null : RunsOn;

    public string[] Sets { get; } = { "Amateur bands", "Broadcast, CB and WWV" };

    private int _set;
    /// <summary>0 = amateur, 1 = broadcast. Swaps the rows and which options apply.</summary>
    public int SetIndex
    {
        get => _set;
        set
        {
            if (!SetProperty(ref _set, value)) return;
            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(IsAmateur));
            OnPropertyChanged(nameof(Blurb));
            OnPropertyChanged(nameof(HfAntennaEnabled));
            OnPropertyChanged(nameof(RecipeEnabled));
        }
    }

    public bool IsAmateur => _set == 0;
    public ObservableCollection<BandRow> Rows => IsAmateur ? Amateur : Broadcast;

    public string Blurb => IsAmateur
        ? "One button per band. With \"change band only\" the radio brings back the frequency, mode, filter and antennas it last had on that band, like the band buttons on the front panel. Otherwise each button is a full recipe with the band's usual spot."
        : "One button per broadcast band: a spot inside the band, AM, on the antenna you pick. The radio has no band memory outside the amateur bands, so these are always full recipes. Every button it makes is an ordinary button you can edit afterwards.";

    private string? _hf;
    public string? HfAntenna { get => _hf; set => SetProperty(ref _hf, value); }

    private string? _xvtA;
    public string? XvtAAntenna { get => _xvtA; set => SetProperty(ref _xvtA, value); }

    private string? _xvtB;
    public string? XvtBAntenna { get => _xvtB; set => SetProperty(ref _xvtB, value); }

    private bool _bandChangeOnly = true;
    /// <summary>Amateur set: one <c>display pan set {pan} band=</c> line per button, nothing else.</summary>
    public bool BandChangeOnly
    {
        get => _bandChangeOnly;
        set
        {
            if (!SetProperty(ref _bandChangeOnly, value)) return;
            OnPropertyChanged(nameof(HfAntennaEnabled));
            OnPropertyChanged(nameof(RecipeEnabled));
        }
    }

    /// <summary>The HF antenna matters for any full recipe, amateur or broadcast.</summary>
    public bool HfAntennaEnabled => !IsAmateur || !BandChangeOnly;

    /// <summary>Transverter ports and CW only matter for an amateur full recipe.</summary>
    public bool RecipeEnabled => IsAmateur && !BandChangeOnly;

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

    public void SelectAll(bool on) { foreach (var b in Rows) b.IsSelected = on; }

    /// <returns>The buttons, and the names of bands skipped because the radio has no transverter
    /// entry for them (band-change mode only).</returns>
    public (List<GeneratedButton> Buttons, List<string> Skipped) Generate()
    {
        var names = Rows.Where(b => b.IsSelected).Select(b => b.Band.Name).ToList();
        if (!IsAmateur)
            return (BandSet.GenerateBroadcast(names, HfAntenna ?? "ANT1", Hotkeys), new List<string>());
        if (BandChangeOnly)
            return BandSet.GenerateBandChange(names, _xvtrs, Hotkeys);
        return (BandSet.Generate(names, HfAntenna ?? "ANT1", XvtAAntenna ?? "XVTA", XvtBAntenna ?? "XVTB", Cw, Hotkeys),
            new List<string>());
    }
}

public sealed class BandRow : ViewModelBase
{
    public BandRow(Band band, bool selected) { Band = band; _selected = selected; }
    public Band Band { get; }
    public string Name => Band.Name;
    public string Detail => Band.PhoneMode == "AM"
        ? $"{Band.PhoneMhz:0.000} AM"
        : Band.Transverter is null
            ? $"{Band.PhoneMhz:0.000} {Band.PhoneMode} / {Band.CwMhz:0.000} CW"
            : $"{Band.PhoneMhz:0.000} {Band.PhoneMode} / {Band.CwMhz:0.000} CW  xvtr {Band.Transverter}";

    private bool _selected;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
}
