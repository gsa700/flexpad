using System.Collections.ObjectModel;
using Avalonia.Media;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.ViewModels;

/// <summary>
/// Edits one button: label, hotkey, colour and the command list. The power-user view: a new button
/// starts in <see cref="NewButtonViewModel"/>, which holds one of these for the shared details.
/// </summary>
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
        _inBandRow = button.InBandRow;
        _runsOn = button.TargetLetter ?? ActiveSlice;
        foreach (var (hex, name) in Reference.Palette)
            Swatches.Add(new Swatch(hex, name, new RelayCommand(() => ColorHex = hex)));
        ClearColorCommand = new RelayCommand(() => ColorHex = "");
        CaptureBasicCommand = new RelayCommand(() => Capture(false));
        CaptureFullCommand = new RelayCommand(() => Capture(true));
        CaptureAllBasicCommand = new RelayCommand(() => CaptureAll(false));
        CaptureAllFullCommand = new RelayCommand(() => CaptureAll(true));
    }

    public bool IsNew { get; }
    public ObservableCollection<Swatch> Swatches { get; } = new();
    public RelayCommand ClearColorCommand { get; }
    public RelayCommand CaptureBasicCommand { get; }
    public RelayCommand CaptureFullCommand { get; }
    public RelayCommand CaptureAllBasicCommand { get; }
    public RelayCommand CaptureAllFullCommand { get; }

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

    public const string ActiveSlice = "Active slice";

    /// <summary>"Active slice", then A to H: which slice the button's {slice} and {pan} mean.</summary>
    public string[] RunsOnChoices { get; } = new[] { ActiveSlice }.Concat(ButtonActions.SliceLetters).ToArray();

    private string _runsOn;
    public string RunsOn { get => _runsOn; set => SetProperty(ref _runsOn, value ?? ActiveSlice); }

    private bool _inBandRow;
    /// <summary>Show this button in the row along the bottom instead of the main grid.</summary>
    public bool InBandRow { get => _inBandRow; set => SetProperty(ref _inBandRow, value); }

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
        "wait 0.5 pauses   slices A B opens/closes slices to match   # starts a comment   hotkey: F1, Ctrl+1, Alt+Shift+X";

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
                _radio.Client.Transmit.ToDictionary(kv => kv.Key, kv => kv.Value), full, now: null, pans: _radio.Client.PanSnapshot());
            Insert(lines);
            if (string.IsNullOrWhiteSpace(Label) || Label == "New") Label = label;
            CaptureStatus = $"Captured {(full ? "full" : "basic")} state of the active slice.";
        }
        catch (SequenceException ex)
        {
            CaptureStatus = ex.Message;
        }
    }

    /// <summary>Every open slice, behind a <c>slices</c> line that opens and closes slices to match.</summary>
    private void CaptureAll(bool full)
    {
        try
        {
            var (label, lines) = SliceCapture.CaptureAll(_radio.Client.Slices,
                _radio.Client.Transmit.ToDictionary(kv => kv.Key, kv => kv.Value), full, now: null, pans: _radio.Client.PanSnapshot());
            Insert(lines);
            if (string.IsNullOrWhiteSpace(Label) || Label == "New") Label = label;
            var n = _radio.Client.Slices.Live().Count;
            CaptureStatus = $"Captured {(full ? "full" : "basic")} state of {n} slice{(n == 1 ? "" : "s")}. The button will open or close slices to match.";
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
        b.Group = InBandRow ? ButtonConfig.BandGroup : null;
        b.Slice = RunsOn == ActiveSlice ? null : RunsOn;
        var lines = CommandsText.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        b.Commands = lines;
    }
}

public sealed record Swatch(string Hex, string Name, RelayCommand PickCommand)
{
    public IBrush Brush => new SolidColorBrush(Color.Parse(Hex));
}
