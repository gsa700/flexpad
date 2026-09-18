using System.Collections.ObjectModel;
using Avalonia.Media;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.ViewModels;

/// <summary>The button grid and the one-line status. Buttons are edited through the app's dialogs.</summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly RadioService _radio;
    private readonly Func<AppConfig> _config;

    public MainWindowViewModel(RadioService radio, Func<AppConfig> config)
    {
        _radio = radio;
        _config = config;
        _radio.StateChanged += RefreshStatus;
        RefreshStatus();
        Rebuild();
    }

    public ObservableCollection<ButtonViewModel> Buttons { get; } = new();

    /// <summary>Buttons tagged for the row along the bottom: the band set lives here.</summary>
    public ObservableCollection<ButtonViewModel> BandButtons { get; } = new();

    private int _columns = 4;
    public int Columns { get => _columns; private set => SetProperty(ref _columns, value); }

    private int _bandColumns = 1;
    /// <summary>One row for up to twelve band buttons; more than that wraps.</summary>
    public int BandColumns { get => _bandColumns; private set => SetProperty(ref _bandColumns, value); }

    private bool _hasBandRow;
    public bool HasBandRow { get => _hasBandRow; private set => SetProperty(ref _hasBandRow, value); }

    private string _statusText = "connecting…";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private IBrush _connBrush = Palette.DimBrush;
    public IBrush ConnBrush { get => _connBrush; private set => SetProperty(ref _connBrush, value); }

    private string _connTip = "";
    public string ConnTip { get => _connTip; private set => SetProperty(ref _connTip, value); }

    private string _radioLabel = "Radio";
    /// <summary>The radio's nickname beside its dot, then model, then a plain word until it answers.</summary>
    public string RadioLabel { get => _radioLabel; private set => SetProperty(ref _radioLabel, value); }

    public string KnobLabel => "FC";

    private IBrush _knobBrush = Palette.DimBrush;
    public IBrush KnobBrush { get => _knobBrush; private set => SetProperty(ref _knobBrush, value); }

    private string _knobTip = "";
    public string KnobTip { get => _knobTip; private set => SetProperty(ref _knobTip, value); }

    private string? _updateAvailable;
    public string? UpdateAvailable { get => _updateAvailable; set { if (SetProperty(ref _updateAvailable, value)) RefreshStatus(); } }

    /// <summary>Raised when the user asks to edit, add, or reorder; the App owns the dialogs.</summary>
    public event Action<ButtonConfig, bool>? EditRequested;   // (button, isNew)

    public void Rebuild()
    {
        var cfg = _config();
        Columns = Math.Max(1, cfg.Columns);
        Buttons.Clear();
        BandButtons.Clear();
        foreach (var b in cfg.Buttons)
            (b.InBandRow ? BandButtons : Buttons).Add(new ButtonViewModel(b, this));
        HasBandRow = BandButtons.Count > 0;
        BandColumns = Math.Clamp(BandButtons.Count, 1, 12);
    }

    public void Fire(ButtonConfig b) => _ = _radio.RunButtonAsync(b);

    public void Edit(ButtonConfig b) => EditRequested?.Invoke(b, false);

    public void Add()
    {
        var b = new ButtonConfig { Label = "New" };
        EditRequested?.Invoke(b, true);
    }

    public void Duplicate(ButtonConfig b)
    {
        var cfg = _config();
        var copy = b.Clone();
        copy.Key = null;
        cfg.Buttons.Insert(cfg.Buttons.IndexOf(b) + 1, copy);
        Changed?.Invoke();
    }

    public void Move(ButtonConfig b, int delta)
    {
        var list = _config().Buttons;
        var i = list.IndexOf(b);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        Changed?.Invoke();
    }

    public void Delete(ButtonConfig b)
    {
        _config().Buttons.Remove(b);
        Changed?.Invoke();
    }

    /// <summary>Pin a button to a slice letter, or null for "whichever slice is active".</summary>
    public void SetSlice(ButtonConfig b, string? letter)
    {
        if (b.TargetLetter == letter) return;
        b.SetTarget(letter);
        Changed?.Invoke();
    }

    /// <summary>
    /// Bulk version of <see cref="SetSlice"/>. With a letter: every button that follows the active
    /// slice runs on that letter instead. With null: every button follows the active slice, the way
    /// all of them did before 0.12. Buttons that never say {slice} or {pan} are left alone.
    /// </summary>
    public int PinAll(string? letter)
    {
        var n = 0;
        foreach (var b in _config().Buttons)
        {
            if (!b.Commands.Any(CommandSequence.UsesOwnSlice)) continue;
            if (letter is null ? b.TargetLetter is null : b.TargetLetter is not null) continue;
            b.SetTarget(letter);
            n++;
        }
        if (n > 0) Changed?.Invoke();
        _radio.Note(letter is null
            ? $"{n} button(s) now follow the active slice"
            : $"{n} button(s) now run on slice {letter}");
        return n;
    }

    /// <summary>Drag-and-drop: <paramref name="item"/> takes <paramref name="target"/>'s slot and row.</summary>
    public void DropOnto(ButtonConfig item, ButtonConfig target)
    {
        if (ReferenceEquals(item, target)) return;
        var changed = ListMoves.MoveOnto(_config().Buttons, item, target);
        if (item.Group != target.Group) { item.Group = target.Group; changed = true; }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Drag-and-drop onto empty space in a row: <paramref name="item"/> goes to that row's end.</summary>
    public void DropInto(ButtonConfig item, bool bandRow)
    {
        var changed = ListMoves.MoveToEnd(_config().Buttons, item);
        var group = bandRow ? ButtonConfig.BandGroup : null;
        if (item.Group != group) { item.Group = group; changed = true; }
        if (changed) Changed?.Invoke();
    }

    /// <summary>The button list changed; the App saves and rebuilds.</summary>
    public event Action? Changed;

    private void RefreshStatus()
    {
        var c = _radio.Client;

        // Two dots carry the connection state; the text is only the slice. The address and the
        // knob's port live in the dots' tooltips so the one line stays short.
        var knob = _radio.KnobState;
        (KnobBrush, KnobTip) = knob switch
        {
            "off" => (Palette.DimBrush, "FlexControl knob is off (Setup, Knob tab)"),
            "not found" => (Palette.DimBrush, "FlexControl knob not found"),
            var k when k.EndsWith(" busy") => (Palette.AmberBrush, $"FlexControl on {k[..^5]} is held by another program"),
            var k when k.EndsWith(" lost") => (Palette.AmberBrush, $"FlexControl on {k[..^5]} was unplugged"),
            var k when k.Contains(':') => (Palette.RedBrush, $"FlexControl: {k}"),
            var k => (Palette.GreenBrush, $"FlexControl on {k}"),
        };

        RadioLabel = c.Nickname is { Length: > 0 } n ? n : c.Model is { Length: > 0 } m ? m : "Radio";

        if (!c.Connected)
        {
            ConnBrush = Palette.RedBrush;
            ConnTip = $"Not connected to {(string.IsNullOrEmpty(c.Host) ? "the radio" : c.Host)}" +
                      (c.Error is { } e ? $": {e}" : "");
            StatusText = $"not connected  {c.Error ?? ""}".TrimEnd();
            return;
        }
        ConnBrush = Palette.GreenBrush;
        ConnTip = $"Connected to {c.Host}:{c.Port}";

        var idx = c.Slices.Active();
        string text;
        if (idx is null) text = "no active slice";
        else
        {
            var s = c.Slices.Get(idx)!;
            string G(string k) => s.GetValueOrDefault(k, "?");
            text = $"slice {G("index_letter")}  {G("RF_frequency")} MHz  {G("mode")}  " +
                   $"rx {G("rxant")}  tx {G("txant")}  step {G("step")}";
        }
        if (UpdateAvailable is { } v) text += $"  ·  update {v} available";
        StatusText = text;
    }

    public void Dispose() => _radio.StateChanged -= RefreshStatus;
}

/// <summary>One grid button. Commands call back into the main view-model.</summary>
public sealed class ButtonViewModel : ViewModelBase
{
    public ButtonViewModel(ButtonConfig config, MainWindowViewModel owner)
    {
        Config = config;
        FireCommand = new RelayCommand(() => owner.Fire(config));
        EditCommand = new RelayCommand(() => owner.Edit(config));
        DuplicateCommand = new RelayCommand(() => owner.Duplicate(config));
        MoveEarlierCommand = new RelayCommand(() => owner.Move(config, -1));
        MoveLaterCommand = new RelayCommand(() => owner.Move(config, +1));
        DeleteCommand = new RelayCommand(() => owner.Delete(config));
        RunsOn = ButtonActions.SliceLetters.Cast<string?>().Append(null)
            .Select(l => new RunsOnChoice(
                (config.TargetLetter == l ? "✓  " : "     ") + (l is null ? "Whichever slice is active" : l == ButtonConfig.DefaultLetter ? "Slice A (default)" : $"Slice {l}"),
                new RelayCommand(() => owner.SetSlice(config, l))))
            .ToList();
    }

    /// <summary>Shown in the button's corner when it does not run on the default slice A: the
    /// letter, or "act" for a button that follows the active slice. Buttons that never address
    /// their own slice show nothing.</summary>
    public string TargetBadge => Config.TargetLetter ?? "act";
    public bool HasTarget => Config.TargetLetter != ButtonConfig.DefaultLetter && Config.Commands.Any(CommandSequence.UsesOwnSlice);
    public List<RunsOnChoice> RunsOn { get; }

    public ButtonConfig Config { get; }
    public string Label => Config.Label;
    public string Hotkey => Config.Key ?? "";
    public bool HasHotkey => !string.IsNullOrWhiteSpace(Config.Key);

    public IBrush Background
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Config.Color) && Color.TryParse(Config.Color, out var c))
                return new SolidColorBrush(c);
            return Palette.PanelBrush;
        }
    }

    public IBrush Foreground => string.IsNullOrWhiteSpace(Config.Color) ? Palette.TextBrush : Brushes.White;

    public RelayCommand FireCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand MoveEarlierCommand { get; }
    public RelayCommand MoveLaterCommand { get; }
    public RelayCommand DeleteCommand { get; }
}

/// <summary>One entry of a button's "Runs on" context submenu.</summary>
public sealed record RunsOnChoice(string Header, RelayCommand Command);
