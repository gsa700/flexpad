// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

using System.IO.Ports;

namespace FlexPad.Core;

/// <summary>
/// Reads a FlexControl knob and reports turns and presses; reconnects on its own.
/// </summary>
/// <remarks>
/// The port is resolved through <c>findPort</c> on every attempt, so a replug or a port renumber
/// is picked up, and a fixed port from config can be passed as a resolver that ignores the search.
/// Events fire on the reader's own thread. Only one program can hold the port: while SmartSDR has
/// it, opening fails and the status reads "busy" until it is released.
/// </remarks>
public sealed class FlexControlReader : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly Func<string?> _findPort;
    private readonly bool _invert;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new();
    private SerialPort? _port;

    /// <summary>Net ticks read in one pass, positive for clockwise (U).</summary>
    public event Action<int>? Turned;
    /// <summary>One of <see cref="KnobProtocol.Events"/>.</summary>
    public event Action<string>? Pressed;
    /// <summary>Human-readable port state for the status line.</summary>
    public event Action<string>? StatusChanged;
    /// <summary>A token the protocol doesn't know, for the log.</summary>
    public event Action<string>? UnknownToken;

    public FlexControlReader(Func<string?> findPort, bool invert)
    {
        _findPort = findPort;
        _invert = invert;
        _thread = new Thread(Run) { Name = "flexcontrol", IsBackground = true };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stop.Set();
        var p = _port;
        _port = null;
        try { p?.Close(); } catch { }
    }

    private void Run()
    {
        while (!_stop.IsSet)
        {
            var name = _findPort();
            if (name is null)
            {
                StatusChanged?.Invoke("not found");
                _stop.Wait(RetryDelay);
                continue;
            }
            SerialPort port;
            try
            {
                port = new SerialPort(name, KnobProtocol.BaudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 200,
                    DtrEnable = true,
                    RtsEnable = true,
                };
                port.Open();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                StatusChanged?.Invoke(ex is UnauthorizedAccessException ? $"{name} busy" : $"{name}: {ex.Message}");
                _stop.Wait(RetryDelay);
                continue;
            }
            _port = port;
            StatusChanged?.Invoke(name);
            try { ReadLoop(port); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                if (_stop.IsSet) break;
                StatusChanged?.Invoke($"{name} lost");
            }
            finally
            {
                _port = null;
                try { port.Close(); } catch { }
            }
            _stop.Wait(TimeSpan.FromSeconds(3));
        }
    }

    private void ReadLoop(SerialPort port)
    {
        var tokenizer = new KnobTokenizer();
        var buf = new byte[256];
        while (!_stop.IsSet && ReferenceEquals(_port, port))
        {
            int n;
            try { n = port.Read(buf, 0, buf.Length); }
            catch (TimeoutException) { continue; }
            if (n <= 0) continue;
            var tokens = tokenizer.Feed(buf.AsSpan(0, n));
            if (tokens.Count == 0) continue;
            var (delta, buttons, unknown) = KnobProtocol.Interpret(tokens);
            foreach (var u in unknown) UnknownToken?.Invoke(u);
            foreach (var b in buttons) Pressed?.Invoke(b);
            if (delta != 0) Turned?.Invoke(_invert ? -delta : delta);
        }
    }
}
