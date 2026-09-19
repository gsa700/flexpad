// FlexPad — programmable command buttons for a FlexRadio.
// Copyright (C) 2026 David Erickson (AB0R). GPL-3.0-or-later; see LICENSE.

using System.Net;
using System.Net.Sockets;
using FlexPad.Core;
using Xunit;

namespace FlexPad.Core.Tests;

public class RadioClientTests
{
    /// <summary>
    /// A refused connection (radio off, or booting: the host answers but nothing listens on 4992)
    /// is reported and retried, never fatal. 0.7.0-beta crashed here: Task.Wait wrapped the
    /// SocketException in an AggregateException that the reconnect loop did not catch, so the
    /// client thread died with an unhandled exception and took the process with it.
    /// </summary>
    [Fact]
    public void RefusedConnectionIsReportedAndSurvived()
    {
        // A loopback port nothing listens on: bind to get a free number, then release it.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var client = new RadioClient("127.0.0.1", port);
        var lost = new ManualResetEventSlim();
        string? note = null;
        client.OnTraffic += (kind, line) =>
        {
            if (kind == Traffic.Note && line.StartsWith("connection lost", StringComparison.Ordinal))
            {
                note = line;
                lost.Set();
            }
        };
        client.Start();

        Assert.True(lost.Wait(TimeSpan.FromSeconds(10)), "the client thread should log the refused connection and keep running");
        Assert.Contains("refused", note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("One or more errors", note);
        Assert.False(client.Connected);
        Assert.NotNull(client.Error);
    }

    /// <summary>
    /// A stand-in radio on a loopback socket that behaves like the 8600M did on 2026-09-19: it accepts a
    /// setting with code 0 and never sends the client that made it a status line for it.
    /// </summary>
    [Fact]
    public void An_accepted_setting_updates_our_own_tables_because_the_radio_never_echoes_it()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var radio = Task.Run(() =>
        {
            using var c = server.AcceptTcpClient();
            using var io = c.GetStream();
            using var reader = new StreamReader(io);
            void Say(string line) { var b = System.Text.Encoding.ASCII.GetBytes(line + '\n'); io.Write(b, 0, b.Length); }
            Say("V1.4.0.0"); Say("H1234ABCD");
            while (reader.ReadLine() is { } line)
            {
                var bar = line.IndexOf('|');
                var seq = line[1..bar]; var cmd = line[(bar + 1)..];
                if (cmd == "sub slice all") Say("S1234ABCD|slice 1 in_use=1 index_letter=B agc_threshold=60 audio_level=15");
                if (cmd == "sub pan all") Say("S1234ABCD|display pan 0x40000001 average=62 bandwidth=0.050000");
                Say(cmd.Contains("bogus") ? $"R{seq}|50000016|" : $"R{seq}|0|");   // accepted, and no status for it: ever
            }
        });

        using var client = new RadioClient("127.0.0.1", port);
        client.Start();
        Assert.True(SpinWait.SpinUntil(() => client.Slices.Get("1") is not null && client.Pans.ContainsKey("0x40000001"), TimeSpan.FromSeconds(10)));
        Assert.Equal("60", client.Slices.Get("1")!["agc_threshold"]);

        Assert.Equal(0, client.Send("slice set 1 agc_threshold=47 audio_level=22").Code);
        Assert.Equal("47", client.Slices.Get("1")!["agc_threshold"]);
        Assert.Equal("22", client.Slices.Get("1")!["audio_level"]);

        Assert.Equal(0, client.Send("display pan set 0x40000001 average=69").Code);
        Assert.Equal("69", client.Pans["0x40000001"]["average"]);
        Assert.Equal("0.050000", client.Pans["0x40000001"]["bandwidth"]);

        Assert.NotEqual(0, client.Send("slice set 1 agc_threshold=99 bogus=1").Code);   // refused: our table must not move
        Assert.Equal("47", client.Slices.Get("1")!["agc_threshold"]);

        client.Dispose();
        server.Stop();
    }
}
