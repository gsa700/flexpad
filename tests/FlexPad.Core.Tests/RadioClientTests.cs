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
}
