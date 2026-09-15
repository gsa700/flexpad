using System.Collections.ObjectModel;
using Avalonia.Threading;
using FlexPad.Core;

namespace FlexPad.App.Services;

public sealed record LogLine(Traffic Tag, string Time, string Text);

/// <summary>
/// The app's one connection to the radio and the knob, plus the console log they feed. Owns the
/// background threads and marshals everything to the UI thread; view-models only ever see UI-thread
/// events and the bounded <see cref="Log"/> collection.
/// </summary>
public sealed class RadioService : IDisposable
{
    private const int LogCapacity = 2000;
    private readonly object _runLock = new();
    private bool _running;
    private RadioClient _client;
    private FlexControlReader? _knob;
    private AppConfig _config;

    public RadioService(AppConfig config)
    {
        _config = config;
        _client = NewClient();
        StartKnob();
    }

    public RadioClient Client => _client;
    public ObservableCollection<LogLine> Log { get; } = new();
    public string KnobState { get; private set; } = "off";

    /// <summary>Raised on the UI thread whenever slice, connection or knob state changed.</summary>
    public event Action? StateChanged;

    /// <summary>Raised on the UI thread with each new log line, for the console to follow.</summary>
    public event Action<LogLine>? Logged;

    // --- radio ---

    private RadioClient NewClient()
    {
        var c = new RadioClient(_config.FlexHost, _config.FlexPort);
        c.OnTraffic += (t, text) => Post(() => Append(t, text));
        c.StateChanged += () => Post(() => StateChanged?.Invoke());
        c.Start();
        return c;
    }

    /// <summary>Apply a changed config: reconnect if the address changed, restart the knob.</summary>
    public void Apply(AppConfig config)
    {
        var addressChanged = config.FlexHost != _client.Host && !(string.IsNullOrEmpty(config.FlexHost) && _client.Connected)
                             || config.FlexPort != _client.Port;
        _config = config;
        if (addressChanged || !_client.Connected)
        {
            _client.Dispose();
            _client = NewClient();
        }
        StartKnob();
    }

    public bool Connected => _client.Connected;

    // --- console ---

    public void Append(Traffic tag, string text)
    {
        var line = new LogLine(tag, DateTime.Now.ToString("HH:mm:ss"), text);
        Log.Add(line);
        while (Log.Count > LogCapacity) Log.RemoveAt(0);
        Logged?.Invoke(line);
    }

    public void Note(string text) => Append(Traffic.Note, text);
    public void Fail(string text) => Append(Traffic.Error, text);

    private static void Post(Action a) => Dispatcher.UIThread.Post(a);

    // --- buttons ---

    /// <summary>Fire a button's sequence on a worker; one at a time. Returns when it finishes.</summary>
    public async Task<bool> RunButtonAsync(ButtonConfig button)
    {
        lock (_runLock)
        {
            if (_running) { Fail("a sequence is still running"); return false; }
            _running = true;
        }
        Note($"--- {button.Label} ---");
        var lines = button.Commands.ToList();
        var stop = _config.StopOnError;
        try
        {
            var ok = await Task.Run(() => CommandSequence.Run(lines, _client.Slices,
                cmd => _client.Send(cmd), stop, err => Post(() => Fail(err))));
            Append(ok ? Traffic.Note : Traffic.Error, $"--- {button.Label}: {(ok ? "done" : "failed")} ---");
            return ok;
        }
        finally
        {
            lock (_runLock) _running = false;
        }
    }

    /// <summary>Send one typed command, with the same placeholders a button gets.</summary>
    public void SendManual(string command)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var (code, text) = _client.Send(CommandSequence.Substitute(command, _client.Slices));
                if (code != 0) Post(() => Fail($"error 0x{code:X} {text}"));
            }
            catch (Exception ex) when (ex is SequenceException or NotConnectedException)
            {
                Post(() => Fail(ex.Message));
            }
        });
    }

    // --- knob ---

    private void StartKnob()
    {
        _knob?.Dispose();
        _knob = null;
        var k = _config.Knob;
        if (!k.Enabled)
        {
            KnobState = "off";
            StateChanged?.Invoke();
            return;
        }
        var fixedPort = string.IsNullOrWhiteSpace(k.Port) ? null : k.Port.Trim();
        var reader = new FlexControlReader(() => fixedPort ?? KnobPort.Find(), k.Invert);
        reader.StatusChanged += s => Post(() => { KnobState = s; Note($"knob: {s}"); StateChanged?.Invoke(); });
        reader.Turned += KnobTurn;
        reader.Pressed += code => Post(() => KnobPress(code));
        reader.UnknownToken += t => Post(() => Append(Traffic.Knob, $"knob sent unknown token '{t}'"));
        reader.Start();
        _knob = reader;
    }

    /// <summary>Runs on the knob thread: retune the active slice by delta steps.</summary>
    private void KnobTurn(int delta)
    {
        var c = _client;
        var idx = c.Slices.Active();
        if (idx is null || !c.Connected) return;
        var s = c.Slices.Get(idx)!;
        var hz = FlexProtocol.ParseMhz(s.GetValueOrDefault("RF_frequency"));
        if (hz is null) return;
        var step = int.TryParse(s.GetValueOrDefault("step"), out var st) ? st : 100;
        var target = KnobPolicy.TuneTarget(hz.Value, delta, step);
        var mhz = FlexProtocol.FormatMhz(target);
        // Optimistic: the next burst of ticks builds on this, not on a status echo still in flight.
        c.Slices.Set(idx, "RF_frequency", mhz);
        try
        {
            var (code, text) = c.Send($"slice tune {idx} {mhz}");
            Post(() =>
            {
                if (code != 0) Fail($"knob tune refused: {text}");
                else Append(Traffic.Knob, $"knob {delta:+#;-#;0} x {step} Hz -> {mhz}");
                StateChanged?.Invoke();
            });
        }
        catch (NotConnectedException) { }
    }

    private void KnobPress(string code)
    {
        var name = KnobProtocol.EventNames.GetValueOrDefault(code, code);
        var binding = (_config.Knob.Bindings.GetValueOrDefault(code) ?? "").Trim();
        if (binding.Length == 0)
        {
            Append(Traffic.Knob, $"{name}: not bound (Setup, Knob tab to assign)");
            return;
        }
        if (binding.StartsWith('@'))
        {
            Note($"{name}: {binding}");
            _ = Task.Run(() =>
            {
                try { KnobAction(binding); }
                catch (NotConnectedException ex) { Post(() => Fail(ex.Message)); }
            });
            return;
        }
        var button = _config.Buttons.FirstOrDefault(b => b.Label == binding);
        if (button is null) { Fail($"{name}: no button labeled '{binding}'"); return; }
        _ = RunButtonAsync(button);
    }

    private void KnobAction(string action)
    {
        var c = _client;
        if (!c.Connected) throw new NotConnectedException("not connected to the radio");
        var idx = c.Slices.Active();
        var ctx = new KnobContext
        {
            SliceIndex = idx,
            Slice = idx is null ? null : c.Slices.Get(idx),
            LiveSlices = c.Slices.Live(),
            Transmit = c.Transmit.ToDictionary(kv => kv.Key, kv => kv.Value),
            Interlock = c.Interlock.ToDictionary(kv => kv.Key, kv => kv.Value),
            Amplifiers = c.Amplifiers.ToDictionary(kv => kv.Key,
                kv => (IReadOnlyDictionary<string, string>)kv.Value.ToDictionary(x => x.Key, x => x.Value)),
            Steps = _config.Knob.Steps,
        };
        KnobPlan plan;
        try { plan = KnobActions.Plan(action, ctx); }
        catch (SequenceException ex) { Post(() => Fail(ex.Message)); return; }

        foreach (var cmd in plan.Commands)
        {
            var (code, text) = c.Send(cmd);
            if (code != 0) { Post(() => Fail($"error 0x{code:X} {text} <- {cmd}")); return; }
        }
        if (plan.Optimistic is { } opt && idx is not null) c.Slices.Set(idx, opt.Key, opt.Value);
        Post(() => { Note(plan.Note); StateChanged?.Invoke(); });
    }

    public void Dispose()
    {
        _knob?.Dispose();
        _client.Dispose();
    }
}
