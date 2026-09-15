// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace FlexPad.Core;

public sealed record RadioInfo(string Ip, string Model, string Nickname, string Callsign, string Version, string Status);

/// <summary>
/// The radio's once-a-second UDP discovery broadcast on port 4992: a VITA-49 packet whose payload,
/// after a 28-byte header, is ASCII <c>key=value</c> pairs. Parsing is separate from listening so it
/// can be tested against a captured packet.
/// </summary>
public static partial class Discovery
{
    public const int Port = 4992;
    private const int HeaderBytes = 28;

    [GeneratedRegex(@"(\w+)=(\S+)")]
    private static partial Regex Pair();

    public static RadioInfo? Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length <= HeaderBytes) return null;
        var text = Encoding.ASCII.GetString(packet[HeaderBytes..]);
        var kv = new Dictionary<string, string>();
        foreach (Match m in Pair().Matches(text)) kv[m.Groups[1].Value] = m.Groups[2].Value;
        if (!kv.TryGetValue("ip", out var ip) || !kv.TryGetValue("model", out var model)) return null;
        kv.TryGetValue("nickname", out var nick);
        kv.TryGetValue("callsign", out var call);
        kv.TryGetValue("version", out var ver);
        kv.TryGetValue("status", out var status);
        return new RadioInfo(ip, model, nick ?? "", call ?? "", ver ?? "", status ?? "");
    }

    /// <summary>
    /// Listen for radios for a while. SmartSDR on the same PC binds the same port, hence the
    /// reuse-address option. Returns what answered, de-duplicated by IP.
    /// </summary>
    public static List<RadioInfo> Listen(TimeSpan window)
    {
        var found = new Dictionary<string, RadioInfo>();
        try
        {
            using var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            sock.Bind(new IPEndPoint(IPAddress.Any, Port));
            sock.ReceiveTimeout = 500;
            var buf = new byte[4096];
            var deadline = DateTime.UtcNow + window;
            while (DateTime.UtcNow < deadline)
            {
                int n;
                try { n = sock.Receive(buf); }
                catch (SocketException) { continue; }   // timeout: keep waiting
                if (Parse(buf.AsSpan(0, n)) is { } r) found[r.Ip] = r;
            }
        }
        catch (SocketException)
        {
            // Port unavailable: nothing to discover from here.
        }
        return found.Values.ToList();
    }
}
