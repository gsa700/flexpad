// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

namespace FlexPad.Core;

/// <summary>
/// The SmartSDR TCP/IP API line protocol (port 4992), as far as this app uses it.
/// </summary>
/// <remarks>
/// Every line is one of: <c>V1.4.0.0</c> (protocol version, once), <c>H1A2B3C4D</c> (our client
/// handle, once), <c>M...|text</c> (a message), <c>R&lt;seq&gt;|&lt;hex code&gt;|&lt;text&gt;</c>
/// (the reply to the command we sent as <c>C&lt;seq&gt;|...</c>), or
/// <c>S&lt;handle&gt;|&lt;object&gt; &lt;key=value...&gt;</c> (a status update after a <c>sub</c>).
/// Status is incremental: a retune sends <c>RF_frequency</c> alone, so slice state must be merged,
/// never replaced. Pure parsing lives here so it can be tested against captured lines.
/// </remarks>
public enum LineKind { Version, Handle, Message, Response, Status, Other }

public sealed record ProtocolLine(LineKind Kind, string Raw)
{
    /// <summary>Response sequence number; -1 when not a response or unparseable.</summary>
    public int Sequence { get; init; } = -1;

    /// <summary>Response code; 0 is success. -1 when not a response or unparseable.</summary>
    public int Code { get; init; } = -1;

    /// <summary>Response text (may be empty), status body after the first '|', or the message text.</summary>
    public string Text { get; init; } = "";
}

public static class FlexProtocol
{
    public static ProtocolLine Parse(string line)
    {
        if (string.IsNullOrEmpty(line)) return new ProtocolLine(LineKind.Other, line ?? "");
        switch (line[0])
        {
            case 'V':
                return new ProtocolLine(LineKind.Version, line) { Text = line[1..] };
            case 'H':
                return new ProtocolLine(LineKind.Handle, line) { Text = line[1..] };
            case 'M':
                return new ProtocolLine(LineKind.Message, line) { Text = AfterBar(line) };
            case 'S':
                return new ProtocolLine(LineKind.Status, line) { Text = AfterBar(line) };
            case 'R':
            {
                // R<seq>|<hex code>|<text>
                var body = line[1..];
                var firstBar = body.IndexOf('|');
                if (firstBar < 0) return new ProtocolLine(LineKind.Other, line);
                var seqText = body[..firstBar];
                var rest = body[(firstBar + 1)..];
                var secondBar = rest.IndexOf('|');
                var codeText = secondBar < 0 ? rest : rest[..secondBar];
                var text = secondBar < 0 ? "" : rest[(secondBar + 1)..];
                var seq = int.TryParse(seqText, out var s) ? s : -1;
                var code = int.TryParse(codeText, System.Globalization.NumberStyles.HexNumber, null, out var c) ? c : -1;
                return new ProtocolLine(LineKind.Response, line) { Sequence = seq, Code = code, Text = text };
            }
            default:
                return new ProtocolLine(LineKind.Other, line);
        }
    }

    private static string AfterBar(string line)
    {
        var i = line.IndexOf('|');
        return i < 0 ? "" : line[(i + 1)..];
    }

    /// <summary>Split <c>a=1 b=2</c> into pairs. Bare tokens without '=' are ignored.</summary>
    public static Dictionary<string, string> KeyValues(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tok in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = tok.IndexOf('=');
            if (eq > 0) d[tok[..eq]] = tok[(eq + 1)..];
        }
        return d;
    }

    /// <summary>Format a frequency in Hz the way the radio wants it: MHz with six decimals.</summary>
    public static string FormatMhz(long hz) =>
        (hz / 1_000_000.0).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Parse the radio's <c>RF_frequency</c> (MHz) to whole Hz. Null when unparseable.</summary>
    public static long? ParseMhz(string? mhz) =>
        double.TryParse(mhz, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? (long)Math.Round(v * 1_000_000)
            : null;
}

/// <summary>
/// The merged state of every slice the radio has reported, keyed by slice index ("0", "1", ...).
/// Thread-safe for the simple use here: the client thread merges, UI and knob threads read.
/// </summary>
public sealed class SliceTable
{
    private readonly Dictionary<string, Dictionary<string, string>> _slices = new();
    private readonly object _lock = new();

    /// <summary>Merge one <c>slice &lt;n&gt; key=value...</c> status body.</summary>
    /// <returns>False when the body is not a slice status.</returns>
    public bool Merge(string statusBody)
    {
        if (!statusBody.StartsWith("slice ", StringComparison.Ordinal)) return false;
        var rest = statusBody[6..];
        var sp = rest.IndexOf(' ');
        var idx = sp < 0 ? rest : rest[..sp];
        var attrs = sp < 0 ? "" : rest[(sp + 1)..];
        lock (_lock)
        {
            if (!_slices.TryGetValue(idx, out var d)) _slices[idx] = d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in FlexProtocol.KeyValues(attrs)) d[k] = v;
        }
        return true;
    }

    public void Clear() { lock (_lock) _slices.Clear(); }

    /// <summary>A copy of one slice's attributes, or null.</summary>
    public IReadOnlyDictionary<string, string>? Get(string index)
    {
        lock (_lock) return _slices.TryGetValue(index, out var d) ? new Dictionary<string, string>(d) : null;
    }

    /// <summary>Set one attribute locally (e.g. an optimistic frequency after a knob tune).</summary>
    public void Set(string index, string key, string value)
    {
        lock (_lock)
        {
            if (!_slices.TryGetValue(index, out var d)) _slices[index] = d = new Dictionary<string, string>(StringComparer.Ordinal);
            d[key] = value;
        }
    }

    /// <summary>Indexes of slices with <c>in_use=1</c>, in numeric order.</summary>
    public List<string> Live()
    {
        lock (_lock)
            return _slices.Where(kv => kv.Value.TryGetValue("in_use", out var u) && u == "1")
                .Select(kv => kv.Key)
                .OrderBy(k => int.TryParse(k, out var n) ? n : int.MaxValue)
                .ToList();
    }

    public string? Active() => FirstLive("active", "1");
    public string? Tx() => FirstLive("tx", "1");
    public string? ByLetter(string letter) => FirstLive("index_letter", letter);

    private string? FirstLive(string key, string value)
    {
        lock (_lock)
            return _slices
                .Where(kv => kv.Value.TryGetValue("in_use", out var u) && u == "1")
                .Where(kv => kv.Value.TryGetValue(key, out var v) && v == value)
                .Select(kv => kv.Key)
                .OrderBy(k => int.TryParse(k, out var n) ? n : int.MaxValue)
                .FirstOrDefault();
    }
}
