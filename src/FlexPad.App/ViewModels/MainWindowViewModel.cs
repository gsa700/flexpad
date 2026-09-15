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

    private int _columns = 4;
    public int Columns { get => _columns; private set => SetProperty(ref _columns, value); }

    private string _statusText = "connecting…";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    private IBrush _connBrush = Palette.DimBrush;
    public IBrush ConnBrush { get => _connBrush; private set => SetProperty(ref _connBrush, value); }

    private string? _updateAvailable;
    public string? UpdateAvailable { get => _updateAvailable; set { if (SetProperty(ref _updateAvailable, value)) RefreshStatus(); } }

    /// <summary>Raised when the user asks to edit, add, or reorder; the App owns the dialogs.</summary>
    public event Action<ButtonConfig, bool>? EditRequested;   // (button, isNew)

    public void Rebuild()
    {
        var cfg = _config();
        Columns = Math.Max(1, cfg.Columns);
        Buttons.Clear();
        foreach (var b in cfg.Buttons) Buttons.Add(new ButtonViewModel(b, this));
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

    /// <summary>The button list changed; the App saves and rebuilds.</summary>
    public event Action? Changed;

    private void RefreshStatus()
    {
        var c = _radio.Client;
        if (!c.Connected)
        {
            StatusText = $"not connected  {c.Error ?? ""}".TrimEnd();
            ConnBrush = Palette.RedBrush;
            return;
        }
        ConnBrush = Palette.GreenBrush;
        var idx = c.Slices.Active();
        if (idx is null)
        {
            StatusText = $"{c.Host}  no active slice  ·  knob {_radio.KnobState}";
            return;
        }
        var s = c.Slices.Get(idx)!;
        string G(string k) => s.GetValueOrDefault(k, "?");
        var text = $"{c.Host}  slice {G("index_letter")}  {G("RF_frequency")} MHz  {G("mode")}  " +
                   $"rx {G("rxant")}  tx {G("txant")}  step {G("step")}  ·  knob {_radio.KnobState}";
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
    }

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
