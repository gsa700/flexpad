using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media;
using FlexPad.App.Services;
using FlexPad.Core;

namespace FlexPad.App.ViewModels;

/// <summary>
/// The traffic log and a command line, in their own window so the button panel stays small for
/// people who never want to see the protocol. The log itself lives in <see cref="RadioService"/>
/// and keeps accumulating whether or not this window is open.
/// </summary>
public sealed class ConsoleViewModel : ViewModelBase, IDisposable
{
    private readonly RadioService _radio;
    private readonly List<string> _history = new();
    private int _historyPos;

    public ConsoleViewModel(RadioService radio)
    {
        _radio = radio;
        SendCommand = new RelayCommand(Send);
        _radio.Log.CollectionChanged += OnLogChanged;
        Rebuild();
    }

    public ObservableCollection<LogRow> Rows { get; } = new();
    public RelayCommand SendCommand { get; }

    private string _command = "";
    public string Command { get => _command; set => SetProperty(ref _command, value); }

    private bool _showStatus;
    public bool ShowStatus
    {
        get => _showStatus;
        set { if (SetProperty(ref _showStatus, value)) Rebuild(); }
    }

    /// <summary>Raised after rows were appended, so the view can keep the end in sight.</summary>
    public event Action? Appended;

    private bool Visible(LogLine l) => ShowStatus || l.Tag is not (Traffic.Status or Traffic.Knob);

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var l in _radio.Log) if (Visible(l)) Rows.Add(new LogRow(l));
        Appended?.Invoke();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            var any = false;
            foreach (LogLine l in e.NewItems) if (Visible(l)) { Rows.Add(new LogRow(l)); any = true; }
            while (Rows.Count > 2000) Rows.RemoveAt(0);
            if (any) Appended?.Invoke();
        }
        else if (e.Action != NotifyCollectionChangedAction.Remove)
        {
            Rebuild();
        }
    }

    private void Send()
    {
        var cmd = Command.Trim();
        if (cmd.Length == 0) return;
        _history.Add(cmd);
        _historyPos = _history.Count;
        Command = "";
        _radio.SendManual(cmd);
    }

    public void HistoryStep(int delta)
    {
        if (_history.Count == 0) return;
        _historyPos = Math.Clamp(_historyPos + delta, 0, _history.Count);
        Command = _historyPos < _history.Count ? _history[_historyPos] : "";
    }

    public void Dispose() => _radio.Log.CollectionChanged -= OnLogChanged;
}

public sealed class LogRow
{
    public LogRow(LogLine line)
    {
        Text = $"{line.Time} {line.Text}";
        Brush = line.Tag switch
        {
            Traffic.Sent => new SolidColorBrush(Color.Parse("#8EC5FF")),
            Traffic.Received => new SolidColorBrush(Color.Parse("#B8E986")),
            Traffic.Status => Palette.DimBrush,
            Traffic.Note => Palette.AmberBrush,
            Traffic.Error => Palette.RedBrush,
            Traffic.Knob => new SolidColorBrush(Color.Parse("#C9A0FF")),
            _ => Palette.TextBrush,
        };
    }

    public string Text { get; }
    public IBrush Brush { get; }
}
