using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using YARG.Net.Tests.TestUtilities;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests;

public sealed class LiteNetLibTransportTests
{
    [Fact]
    public void ClientConnectingToServer_RaisesServerPeerConnected()
    {
        var port = PortAllocator.GetFreeTcpPort();

        using var server = new LiteNetLibTransport();
        using var client = new LiteNetLibTransport();

        var connectedTcs = new TaskCompletionSource<bool>();
        server.OnPeerConnected += _ => connectedTcs.TrySetResult(true);

        server.Start(new TransportStartOptions
        {
            Port = port,
            IsServer = true,
            Address = "0.0.0.0",
        });

        client.Start(new TransportStartOptions
        {
            Address = "127.0.0.1",
            Port = port,
            IsServer = false,
        });

        var connected = WaitFor(() => connectedTcs.Task.IsCompleted, server, client, TimeSpan.FromSeconds(2));
        Assert.True(connected, "Server never observed the client connection.");
    }

    private static bool WaitFor(Func<bool> predicate, LiteNetLibTransport server, LiteNetLibTransport client, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            server.Poll(TimeSpan.Zero);
            client.Poll(TimeSpan.Zero);

            if (predicate())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        server.Poll(TimeSpan.Zero);
        client.Poll(TimeSpan.Zero);
        return predicate();
    }
}
