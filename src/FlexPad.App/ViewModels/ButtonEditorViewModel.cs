using System.Collections.ObjectModel;
using Avalonia.Media;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.ViewModels;

/// <summary>Edits one button: label, hotkey, colour and the command list.</summary>
public sealed class ButtonEditorViewModel : ViewModelBase
{
    private readonly RadioService _radio;

    public ButtonEditorViewModel(ButtonConfig button, RadioService radio, bool isNew)
    {
        _radio = radio;
        IsNew = isNew;
        _label = button.Label;
        _hotkey = button.Key ?? "";
        _colorHex = button.Color ?? "";
        _commandsText = string.Join("\n", button.Commands);
        foreach (var (hex, name) in Reference.Palette)
            Swatches.Add(new Swatch(hex, name, new RelayCommand(() => ColorHex = hex)));
        ClearColorCommand = new RelayCommand(() => ColorHex = "");
        CaptureBasicCommand = new RelayCommand(() => Capture(false));
        CaptureFullCommand = new RelayCommand(() => Capture(true));
        BuildCommand = new RelayCommand(Build);
        PrepareBuilder();
    }

    // --- build from choices ---
    //
    // The lists come from the active slice's status, so they are the radio's own; the fields are
    // prefilled from the slice too, so "make a button for where I am now" is a Build and a Save.

    public List<string> Modes { get; private set; } = new();
    public List<string> RxAntennas { get; private set; } = new();
    public List<string> TxAntennas { get; private set; } = new();
    public List<string> StepChoices { get; } = new[] { "" }.Concat(ButtonBuilder.StepChoices.Select(s => s.ToString())).ToList();
    public RelayCommand BuildCommand { get; }

    private string _buildFreq = "";
    public string BuildFreq { get => _buildFreq; set => SetProperty(ref _buildFreq, value); }

    private string? _buildMode;
    public string? BuildMode
    {
        get => _buildMode;
        set
        {
            if (!SetProperty(ref _buildMode, value) || value is null) return;
            // A new mode brings its usual filter; the user can still type over it.
            var (lo, hi) = ButtonBuilder.DefaultFilter(value);
            BuildFiltLo = lo.ToString();
            BuildFiltHi = hi.ToString();
        }
    }

    private string? _buildRx;
    public string? BuildRxAnt { get => _buildRx; set => SetProperty(ref _buildRx, value); }

    private string? _buildTx;
    public string? BuildTxAnt { get => _buildTx; set => SetProperty(ref _buildTx, value); }

    private string _buildFiltLo = "";
    public string BuildFiltLo { get => _buildFiltLo; set => SetProperty(ref _buildFiltLo, value); }

    private string _buildFiltHi = "";
    public string BuildFiltHi { get => _buildFiltHi; set => SetProperty(ref _buildFiltHi, value); }

    private string? _buildStep = "";
    public string? BuildStep { get => _buildStep; set => SetProperty(ref _buildStep, value); }

    private void PrepareBuilder()
    {
        var slices = _radio.Client.Slices;
        var idx = slices.Active();
        var s = idx is null ? null : slices.Get(idx);
        Modes = ButtonBuilder.ListFrom(s, "mode_list", ButtonBuilder.DefaultModes);
        RxAntennas = ButtonBuilder.ListFrom(s, "ant_list", ButtonBuilder.DefaultAntennas);
        TxAntennas = ButtonBuilder.ListFrom(s, "tx_ant_list", ButtonBuilder.DefaultAntennas);
        string G(string k, string d) => s is not null && s.TryGetValue(k, out var v) ? v : d;

        var mode = G("mode", "USB");
        _buildMode = Modes.Contains(mode) ? mode : Modes.FirstOrDefault();
        var (lo, hi) = ButtonBuilder.DefaultFilter(_buildMode ?? "USB");
        BuildFreq = G("RF_frequency", "14.250000");
        BuildRxAnt = RxAntennas.Contains(G("rxant", "")) ? G("rxant", "") : RxAntennas.FirstOrDefault();
        BuildTxAnt = TxAntennas.Contains(G("txant", "")) ? G("txant", "") : TxAntennas.FirstOrDefault();
        BuildFiltLo = G("filter_lo", lo.ToString());
        BuildFiltHi = G("filter_hi", hi.ToString());
        BuildStep = "";
    }

    private void Build()
    {
        try
        {
            if (!int.TryParse(BuildFiltLo, out var lo) || !int.TryParse(BuildFiltHi, out var hi))
                throw new SequenceException("filter edges must be whole numbers of Hz");
            var step = int.TryParse(BuildStep, out var st) ? st : 0;
            var (label, lines) = ButtonBuilder.Build(BuildFreq, BuildMode ?? "", BuildRxAnt ?? "", BuildTxAnt ?? "", lo, hi, step);
            Insert(lines);
            if (string.IsNullOrWhiteSpace(Label) || Label == "New") Label = label;
            CaptureStatus = $"Built {lines.Count} lines from your choices.";
        }
        catch (SequenceException ex)
        {
            CaptureStatus = ex.Message;
        }
    }

    public bool IsNew { get; }
    public ObservableCollection<Swatch> Swatches { get; } = new();
    public RelayCommand ClearColorCommand { get; }
    public RelayCommand CaptureBasicCommand { get; }
    public RelayCommand CaptureFullCommand { get; }

    private string _label;
    public string Label { get => _label; set => SetProperty(ref _label, value); }

    private string _hotkey;
    public string Hotkey { get => _hotkey; set => SetProperty(ref _hotkey, value); }

    private string _colorHex;
    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (!SetProperty(ref _colorHex, value)) return;
            OnPropertyChanged(nameof(PreviewBackground));
            OnPropertyChanged(nameof(PreviewForeground));
            OnPropertyChanged(nameof(ColorValid));
        }
    }

    private string _commandsText;
    public string CommandsText { get => _commandsText; set => SetProperty(ref _commandsText, value); }

    private string _captureStatus = "";
    public string CaptureStatus { get => _captureStatus; private set => SetProperty(ref _captureStatus, value); }

    public bool ColorValid => string.IsNullOrWhiteSpace(ColorHex) || Color.TryParse(ColorHex.Trim(), out _);

    public IBrush PreviewBackground =>
        !string.IsNullOrWhiteSpace(ColorHex) && Color.TryParse(ColorHex.Trim(), out var c)
            ? new SolidColorBrush(c)
            : string.IsNullOrWhiteSpace(ColorHex) ? Palette.PanelBrush : Palette.RedBrush;

    public IBrush PreviewForeground => string.IsNullOrWhiteSpace(ColorHex) ? Palette.TextBrush : Brushes.White;

    public string Hint =>
        "{slice} = active slice   {tx} = transmit slice   {A}..{H} = slice by letter\n" +
        "wait 0.5 pauses   # starts a comment   hotkey: F1, Ctrl+1, Alt+Shift+X";

    /// <summary>Append lines on a fresh line, never splitting one the user is typing.</summary>
    public void Insert(IEnumerable<string> lines)
    {
        var current = CommandsText;
        var joined = string.Join("\n", lines);
        CommandsText = current.Length == 0 ? joined + "\n"
            : current.EndsWith('\n') ? current + joined + "\n"
            : current + "\n" + joined + "\n";
    }

    private void Capture(bool full)
    {
        try
        {
            var (label, lines) = SliceCapture.Capture(_radio.Client.Slices,
                _radio.Client.Transmit.ToDictionary(kv => kv.Key, kv => kv.Value), full);
            Insert(lines);
            if (string.IsNullOrWhiteSpace(Label) || Label == "New") Label = label;
            CaptureStatus = $"Captured {(full ? "full" : "basic")} state of the active slice.";
        }
        catch (SequenceException ex)
        {
            CaptureStatus = ex.Message;
        }
    }

    /// <summary>Write the edits back. Blank lines at the end are dropped; the rest are kept verbatim.</summary>
    public void ApplyTo(ButtonConfig b)
    {
        b.Label = string.IsNullOrWhiteSpace(Label) ? "?" : Label.Trim();
        b.Key = string.IsNullOrWhiteSpace(Hotkey) ? null : Hotkey.Trim();
        b.Color = string.IsNullOrWhiteSpace(ColorHex) ? null : ColorHex.Trim();
        var lines = CommandsText.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        b.Commands = lines;
    }
}

public sealed record Swatch(string Hex, string Name, RelayCommand PickCommand)
{
    public IBrush Brush => new SolidColorBrush(Color.Parse(Hex));
}
