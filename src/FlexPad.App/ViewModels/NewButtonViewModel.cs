using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.ViewModels;

public enum NewButtonKind { Frequency, Snapshot, Band, Action, Blank }

/// <summary>
/// The guided start for a new button (David, 2026-09-18: the editor had become "really busy and
/// confusing"). Page one asks what the button should do, with "go to a frequency" preselected and
/// prefilled from the active slice, so Next then Save makes a working button. Page two names it
/// and shows the commands it will send. The full editor is one click away and is where an existing
/// button is edited; this window only ever makes new ones.
/// </summary>
public sealed class NewButtonViewModel : ViewModelBase
{
    private readonly RadioService _radio;
    private readonly Dictionary<string, string> _xvtrs;
    private string? _autoLabel;

    public NewButtonViewModel(ButtonConfig button, RadioService radio)
    {
        _radio = radio;
        _xvtrs = radio.Client.XvtrIndexByName();
        Details = new ButtonEditorViewModel(button, radio, isNew: true);

        var slices = radio.Client.Slices;
        var idx = slices.Active();
        var s = idx is null ? null : slices.Get(idx);
        Modes = ButtonBuilder.ListFrom(s, "mode_list", ButtonBuilder.DefaultModes);
        RxAntennas = ButtonBuilder.ListFrom(s, "ant_list", ButtonBuilder.DefaultAntennas);
        TxAntennas = ButtonBuilder.ListFrom(s, "tx_ant_list", ButtonBuilder.DefaultAntennas);
        string G(string k, string d) => s is not null && s.TryGetValue(k, out var v) ? v : d;

        var mode = G("mode", "USB");
        _mode = Modes.Contains(mode) ? mode : Modes.FirstOrDefault();
        var (lo, hi) = ButtonBuilder.DefaultFilter(_mode ?? "USB");
        _freq = G("RF_frequency", "14.250000");
        _rxAnt = RxAntennas.Contains(G("rxant", "")) ? G("rxant", "") : RxAntennas.FirstOrDefault();
        _txAnt = TxAntennas.Contains(G("txant", "")) ? G("txant", "") : TxAntennas.FirstOrDefault();
        _filtLo = G("filter_lo", lo.ToString());
        _filtHi = G("filter_hi", hi.ToString());

        Bands = BandSet.Bands.Select(b => b.Name).ToList();
        _band = "20m";
        _action = Actions[0];
        _actionValue = DefaultFor(_action);
    }

    /// <summary>Label, colour, hotkey, row and the command text: the same state the editor works on.</summary>
    public ButtonEditorViewModel Details { get; }

    // --- pages ---

    private int _page;
    public bool IsChoosing => _page == 0;
    public bool IsNaming => _page == 1;

    private void GoTo(int page)
    {
        _page = page;
        OnPropertyChanged(nameof(IsChoosing));
        OnPropertyChanged(nameof(IsNaming));
    }

    public void Back() { Error = ""; GoTo(0); }

    /// <summary>
    /// Leave page one. Returns "edit" when the choice was to write the commands by hand (the caller
    /// opens the editor), otherwise null: either page two is showing or <see cref="Error"/> says why not.
    /// </summary>
    public string? Next()
    {
        Error = "";
        if (Kind == NewButtonKind.Blank) return "edit";
        try
        {
            var (label, lines, color) = Generate();
            Details.CommandsText = string.Join("\n", lines) + "\n";
            if (string.IsNullOrWhiteSpace(Details.Label) || Details.Label == "New" || Details.Label == _autoLabel)
                Details.Label = _autoLabel = label;
            if (color is not null && string.IsNullOrWhiteSpace(Details.ColorHex)) Details.ColorHex = color;
            Details.InBandRow = Kind == NewButtonKind.Band;
            // A button that says {slice} or {pan} is pinned to the slice it was made from, so it
            // still lands there when another slice is active later. Ones that name their slices
            // by letter (all-slices capture, TX to B) have nothing to pin.
            var usesOwnSlice = lines.Any(l => l.Contains("{slice}") || l.Contains("{pan}"));
            Details.RunsOn = usesOwnSlice ? ActiveLetter() ?? ButtonEditorViewModel.ActiveSlice : ButtonEditorViewModel.ActiveSlice;
            GoTo(1);
        }
        catch (SequenceException ex)
        {
            Error = ex.Message;
        }
        return null;
    }

    private string? ActiveLetter()
    {
        var slices = _radio.Client.Slices;
        var idx = slices.Active();
        var letter = idx is null ? null : slices.Get(idx)?.GetValueOrDefault("index_letter");
        return string.IsNullOrEmpty(letter) ? null : letter;
    }

    private string _error = "";
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    // --- what should it do ---

    private NewButtonKind _kind = NewButtonKind.Frequency;
    public NewButtonKind Kind
    {
        get => _kind;
        set
        {
            if (!SetProperty(ref _kind, value)) return;
            foreach (var n in new[] { nameof(IsFrequency), nameof(IsSnapshot), nameof(IsBand), nameof(IsAction), nameof(IsBlank) })
                OnPropertyChanged(n);
            Error = "";
        }
    }

    // One bool per radio button: a radio button being unchecked writes false, which is ignored.
    public bool IsFrequency { get => _kind == NewButtonKind.Frequency; set { if (value) Kind = NewButtonKind.Frequency; } }
    public bool IsSnapshot { get => _kind == NewButtonKind.Snapshot; set { if (value) Kind = NewButtonKind.Snapshot; } }
    public bool IsBand { get => _kind == NewButtonKind.Band; set { if (value) Kind = NewButtonKind.Band; } }
    public bool IsAction { get => _kind == NewButtonKind.Action; set { if (value) Kind = NewButtonKind.Action; } }
    public bool IsBlank { get => _kind == NewButtonKind.Blank; set { if (value) Kind = NewButtonKind.Blank; } }

    // Go to a frequency: the radio's own lists, prefilled from the active slice.
    public List<string> Modes { get; }
    public List<string> RxAntennas { get; }
    public List<string> TxAntennas { get; }
    public List<string> StepChoices { get; } = new[] { "" }.Concat(ButtonBuilder.StepChoices.Select(s => s.ToString())).ToList();

    private string _freq;
    public string Freq { get => _freq; set => SetProperty(ref _freq, value); }

    private string? _mode;
    public string? Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value) || value is null) return;
            var (lo, hi) = ButtonBuilder.DefaultFilter(value);   // a new mode brings its usual filter
            FiltLo = lo.ToString();
            FiltHi = hi.ToString();
        }
    }

    private string? _rxAnt;
    public string? RxAnt { get => _rxAnt; set => SetProperty(ref _rxAnt, value); }

    private string? _txAnt;
    public string? TxAnt { get => _txAnt; set => SetProperty(ref _txAnt, value); }

    private string _filtLo;
    public string FiltLo { get => _filtLo; set => SetProperty(ref _filtLo, value); }

    private string _filtHi;
    public string FiltHi { get => _filtHi; set => SetProperty(ref _filtHi, value); }

    private string? _step = "";
    public string? Step { get => _step; set => SetProperty(ref _step, value); }

    // Remember what the radio is doing now.
    public string[] SnapshotScopes { get; } = { "The active slice", "Every open slice (opens and closes slices to match)" };
    public string[] SnapshotDetails { get; } =
    {
        "Basic: frequency, mode, antennas, filter",
        "Full: adds step, AGC, noise tools, RF gain, DAX, TX power and the scope's width and centre",
    };

    private int _snapshotScope;
    public int SnapshotScope { get => _snapshotScope; set => SetProperty(ref _snapshotScope, value); }

    private int _snapshotDetail;
    public int SnapshotDetail { get => _snapshotDetail; set => SetProperty(ref _snapshotDetail, value); }

    // Change band.
    public List<string> Bands { get; }

    private string? _band;
    public string? Band { get => _band; set => SetProperty(ref _band, value); }

    // A common action.
    public ButtonAction[] Actions => ButtonActions.All;
    public string[] SliceLetters => ButtonActions.SliceLetters;

    private ButtonAction _action;
    public ButtonAction Action
    {
        get => _action;
        set
        {
            if (value is null || !SetProperty(ref _action, value)) return;
            ActionValue = DefaultFor(value);
            foreach (var n in new[] { nameof(ActionDescription), nameof(ActionParameterLabel),
                         nameof(ActionTakesAntenna), nameof(ActionTakesLetter), nameof(ActionTakesPercent) })
                OnPropertyChanged(n);
        }
    }

    private string? _actionValue;
    public string? ActionValue { get => _actionValue; set => SetProperty(ref _actionValue, value); }

    public string ActionDescription => _action.Description;
    public string ActionParameterLabel => _action.ParameterLabel;
    public bool ActionTakesAntenna => _action.Parameter == ActionParameter.Antenna;
    public bool ActionTakesLetter => _action.Parameter == ActionParameter.SliceLetter;
    public bool ActionTakesPercent => _action.Parameter == ActionParameter.Percent;

    private string DefaultFor(ButtonAction a) =>
        a.Parameter == ActionParameter.Antenna && !TxAntennas.Contains(a.DefaultValue)
            ? TxAntennas.FirstOrDefault() ?? a.DefaultValue
            : a.DefaultValue;

    private (string Label, List<string> Lines, string? Color) Generate()
    {
        var transmit = _radio.Client.Transmit.ToDictionary(kv => kv.Key, kv => kv.Value);
        switch (Kind)
        {
            case NewButtonKind.Frequency:
                if (!int.TryParse(FiltLo, out var lo) || !int.TryParse(FiltHi, out var hi))
                    throw new SequenceException("filter edges must be whole numbers of Hz");
                var step = int.TryParse(Step, out var st) ? st : 0;
                var built = ButtonBuilder.Build(Freq, Mode ?? "", RxAnt ?? "", TxAnt ?? "", lo, hi, step);
                return (built.Label, built.Lines, null);

            case NewButtonKind.Snapshot:
                var full = SnapshotDetail == 1;
                var shot = SnapshotScope == 1
                    ? SliceCapture.CaptureAll(_radio.Client.Slices, transmit, full, now: null, pans: _radio.Client.PanSnapshot())
                    : SliceCapture.Capture(_radio.Client.Slices, transmit, full, now: null, pans: _radio.Client.PanSnapshot());
                return (shot.Label, shot.Lines, null);

            case NewButtonKind.Band:
                var (buttons, _) = BandSet.GenerateBandChange(new[] { Band ?? "" }, _xvtrs, hotkeys: false);
                if (buttons.Count == 0)
                    throw new SequenceException(string.IsNullOrEmpty(Band) ? "choose a band"
                        : $"the radio has no transverter band named {Band}; define it in SmartSDR first");
                return (buttons[0].Label, buttons[0].Lines, buttons[0].Color);

            case NewButtonKind.Action:
                var made = Action.Make(ActionValue ?? "");
                return (made.Label, made.Lines, null);

            default:
                return ("New", new List<string>(), null);
        }
    }
}
