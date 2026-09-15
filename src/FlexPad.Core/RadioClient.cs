// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace FlexPad.Core;

public sealed class NotConnectedException(string message) : Exception(message);

/// <summary>Direction tags for the traffic log, matching the console's colouring.</summary>
public enum Traffic { Sent, Received, Status, Note, Error, Knob }

/// <summary>
/// One TCP session to the radio's API port, reconnecting on its own.
/// </summary>
/// <remarks>
/// <see cref="Send"/> is synchronous: it waits for the <c>R&lt;seq&gt;|</c> reply matched by
/// sequence number, so a slow reply never lands on the wrong command. Status lines are merged into
/// <see cref="Slices"/> and <see cref="Transmit"/>. Every line in or out is offered to
/// <see cref="OnTraffic"/>; state changes raise <see cref="StateChanged"/>. All events fire on the
/// client's own thread — the UI marshals.
/// </remarks>
public sealed class RadioClient : IDisposable
{
    public const int DefaultPort = 4992;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new();
    private readonly object _lock = new();
    private readonly Dictionary<int, (ManualResetEventSlim Done, (int Code, string Text)[] Holder)> _pending = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _seq;

    public string Host { get; private set; }
    public int Port { get; }
    public bool Connected { get; private set; }
    public string? Error { get; private set; }
    public string? Version { get; private set; }
    public string? Handle { get; private set; }
    public SliceTable Slices { get; } = new();
    public ConcurrentDictionary<string, string> Transmit { get; } = new();

    public event Action<Traffic, string>? OnTraffic;
    public event Action? StateChanged;

    /// <param name="host">Radio address; blank means discover it on the LAN.</param>
    public RadioClient(string host, int port = DefaultPort)
    {
        Host = host;
        Port = port;
        _thread = new Thread(Run) { Name = "flex-client", IsBackground = true };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stop.Set();
        Close();
    }

    private void Close()
    {
        var was = Connected;
        Connected = false;
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null;
        _tcp = null;
        lock (_lock)
        {
            foreach (var (done, holder) in _pending.Values) { holder[0] = (-1, "disconnected"); done.Set(); }
            _pending.Clear();
        }
        if (was) StateChanged?.Invoke();
    }

    private void Run()
    {
        while (!_stop.IsSet)
        {
            try { Session(); }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                if (_stop.IsSet) break;
                Error = ex.Message;
                OnTraffic?.Invoke(Traffic.Note, $"connection lost: {ex.Message}");
            }
            Close();
            if (_stop.IsSet) break;
            _stop.Wait(ReconnectDelay);
        }
    }

    private void Session()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            var radios = Discovery.Listen(TimeSpan.FromSeconds(4));
            if (radios.Count == 0)
            {
                Error = "no radio found";
                StateChanged?.Invoke();
                return;
            }
            Host = radios[0].Ip;
            OnTraffic?.Invoke(Traffic.Note, $"discovered {radios[0].Model} {radios[0].Nickname} at {Host}");
        }
        OnTraffic?.Invoke(Traffic.Note, $"connecting to {Host}:{Port}");
        var tcp = new TcpClient();
        if (!tcp.ConnectAsync(Host, Port).Wait(TimeSpan.FromSeconds(10)))
            throw new SocketException((int)SocketError.TimedOut);
        var stream = tcp.GetStream();
        stream.ReadTimeout = 1000;
        _tcp = tcp;
        _stream = stream;
        Slices.Clear();
        Transmit.Clear();
        Error = null;
        Connected = true;
        StateChanged?.Invoke();

        Send("sub slice all", wait: false);
        Send("sub tx all", wait: false);

        var buf = new byte[8192];
        var pending = new StringBuilder();
        while (!_stop.IsSet && ReferenceEquals(_stream, stream))
        {
            int n;
            try { n = stream.Read(buf, 0, buf.Length); }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            { continue; }
            if (n == 0) throw new IOException("radio closed the connection");
            pending.Append(Encoding.UTF8.GetString(buf, 0, n));
            var text = pending.ToString();
            int nl;
            while ((nl = text.IndexOf('\n')) >= 0)
            {
                var line = text[..nl].TrimEnd('\r');
                text = text[(nl + 1)..];
                HandleLine(line);
            }
            pending.Clear();
            pending.Append(text);
        }
    }

    private void HandleLine(string line)
    {
        if (line.Length == 0) return;
        var p = FlexProtocol.Parse(line);
        switch (p.Kind)
        {
            case LineKind.Response:
                OnTraffic?.Invoke(Traffic.Received, line);
                (ManualResetEventSlim Done, (int, string)[] Holder)? entry = null;
                lock (_lock)
                {
                    if (_pending.Remove(p.Sequence, out var e)) entry = e;
                }
                if (entry is { } en) { en.Holder[0] = (p.Code, p.Text); en.Done.Set(); }
                break;
            case LineKind.Status:
                OnTraffic?.Invoke(Traffic.Status, line);
                if (Slices.Merge(p.Text)) StateChanged?.Invoke();
                else if (p.Text.StartsWith("transmit ", StringComparison.Ordinal))
                    foreach (var (k, v) in FlexProtocol.KeyValues(p.Text[9..])) Transmit[k] = v;
                break;
            case LineKind.Version:
                Version = p.Text;
                OnTraffic?.Invoke(Traffic.Received, line);
                break;
            case LineKind.Handle:
                Handle = p.Text;
                OnTraffic?.Invoke(Traffic.Received, line);
                break;
            default:
                OnTraffic?.Invoke(Traffic.Received, line);
                break;
        }
    }

    /// <summary>Send one command and wait for its reply. Code 0 is success.</summary>
    /// <exception cref="NotConnectedException">Not connected, or the socket failed.</exception>
    public (int Code, string Text) Send(string command, bool wait = true)
    {
        var stream = _stream;
        if (stream is null || !Connected) throw new NotConnectedException("not connected to the radio");

        int seq;
        ManualResetEventSlim? done = null;
        var holder = new (int, string)[1];
        lock (_lock)
        {
            seq = ++_seq;
            if (wait)
            {
                done = new ManualResetEventSlim();
                _pending[seq] = (done, holder);
            }
        }
        var wire = $"C{seq}|{command}\n";
        OnTraffic?.Invoke(Traffic.Sent, wire.TrimEnd());
        try { stream.Write(Encoding.UTF8.GetBytes(wire)); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            lock (_lock) _pending.Remove(seq);
            throw new NotConnectedException(ex.Message);
        }
        if (!wait) return (0, "");
        if (!done!.Wait(CommandTimeout))
        {
            lock (_lock) _pending.Remove(seq);
            return (-1, $"no reply within {CommandTimeout.TotalSeconds:0}s");
        }
        return holder[0];
    }
}
