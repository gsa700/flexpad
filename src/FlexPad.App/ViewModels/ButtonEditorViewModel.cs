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
