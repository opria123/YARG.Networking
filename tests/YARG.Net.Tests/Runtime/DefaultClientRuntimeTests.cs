using System;
using System.Threading;
using System.Threading.Tasks;
using YARG.Net.Packets;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Serialization;
using YARG.Net.Handlers.Client;
using YARG.Net.Runtime;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Runtime;

public sealed class DefaultClientRuntimeTests
{
    [Fact]
    public async Task ConnectAsync_WaitsUntilTransportReportsPeer()
    {
        var runtime = new DefaultClientRuntime(TimeSpan.FromMilliseconds(1));
        using var transport = new TestTransport();
        runtime.RegisterTransport(transport);

        var connectTask = runtime.ConnectAsync("127.0.0.1", 8123);
        Assert.False(connectTask.IsCompleted);

        transport.RaiseConnected();
        await connectTask;
        Assert.True(transport.IsRunning);

        await runtime.DisconnectAsync("bye");
        Assert.Equal("bye", transport.LastShutdownReason);
        Assert.False(transport.IsRunning);
    }

    [Fact]
    public async Task ConnectWithoutRegister_Throws()
    {
        var runtime = new DefaultClientRuntime();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ConnectAsync("127.0.0.1", 5000));
    }

    [Fact]
    public async Task ConcurrentConnectCalls_Throw()
    {
        var runtime = new DefaultClientRuntime();
        using var transport = new TestTransport();
        runtime.RegisterTransport(transport);

        var firstConnect = runtime.ConnectAsync("127.0.0.1", 9000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.ConnectAsync("127.0.0.1", 9000));

        transport.RaiseConnected();
        await runtime.DisconnectAsync();
        await firstConnect;
    }

    [Fact]
    public async Task CancellationDuringConnect_ShutsDownTransport()
    {
        var runtime = new DefaultClientRuntime(TimeSpan.FromMilliseconds(1));
        using var transport = new TestTransport();
        runtime.RegisterTransport(transport);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.ConnectAsync("127.0.0.1", 7777, cts.Token));
        Assert.False(transport.IsRunning);
    }

    [Fact]
    public async Task DispatcherReceivesPayloads()
    {
        var runtime = new DefaultClientRuntime();
        using var transport = new TestTransport();
        runtime.RegisterTransport(transport);

        var serializer = new JsonNetSerializer();
        var dispatcher = new PacketDispatcher(serializer);
        var handled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        dispatcher.RegisterHandler<HeartbeatPacket>(PacketType.Heartbeat, (context, envelope, _) =>
        {
            handled.TrySetResult(envelope.Payload.TimestampUnixMs > 0 && context.Role == PacketEndpointRole.Client);
            return Task.CompletedTask;
        });

        runtime.RegisterPacketDispatcher(dispatcher);

        var envelope = PacketEnvelope<HeartbeatPacket>.Create(PacketType.Heartbeat, new HeartbeatPacket(123));
        transport.RaisePayload(serializer.Serialize(envelope));

        var result = await handled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(result);
    }

    [Fact]
    public async Task ConnectionEvents_Fire()
    {
        var runtime = new DefaultClientRuntime();
        using var transport = new TestTransport();
        runtime.RegisterTransport(transport);

        INetConnection? connectedPeer = null;
        var disconnected = false;

        runtime.Connected += (_, args) => connectedPeer = args.Connection;
        runtime.Disconnected += (_, _) => disconnected = true;

        var connectTask = runtime.ConnectAsync("127.0.0.1", 8123);
        transport.RaiseConnected();
        await connectTask;

        Assert.NotNull(connectedPeer);
        Assert.Equal(connectedPeer, runtime.ActiveConnection);

        transport.RaiseDisconnected();
        Assert.True(disconnected);
    }

    [Fact]
    public async Task SessionContext_ClearedOnDisconnect()
    {
        var runtime = new DefaultClientRuntime();
        using var transport = new TestTransport();
        runtime.RegisterTransport(transport);

        var sessionContext = new ClientSessionContext();
        runtime.RegisterSessionContext(sessionContext);

        var connectTask = runtime.ConnectAsync("127.0.0.1", 7000);
        transport.RaiseConnected();
        await connectTask;

        var sessionId = Guid.NewGuid();
        Assert.True(sessionContext.TrySetSession(sessionId));

        await runtime.DisconnectAsync();
        Assert.False(sessionContext.HasSession);
        Assert.Null(sessionContext.SessionId);
    }

    [Fact]
    public async Task HandshakeResponses_UpdateSessionContext()
    {
        var runtime = new DefaultClientRuntime();
        var sessionContext = new ClientSessionContext();
        runtime.RegisterSessionContext(sessionContext);

        var serializer = new JsonNetSerializer();
        var dispatcher = new PacketDispatcher(serializer);
        runtime.RegisterPacketDispatcher(dispatcher);

        var sessionId = Guid.NewGuid();
        var envelope = PacketEnvelope<HandshakeResponsePacket>.Create(
            PacketType.HandshakeResponse,
            new HandshakeResponsePacket(true, null, sessionId));

        var bytes = serializer.Serialize(envelope);
        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client);

        var invoked = false;
        runtime.HandshakeCompleted += (_, args) =>
        {
            invoked = true;
            Assert.True(args.Accepted);
            Assert.Equal(sessionId, args.SessionId);
        };

        await dispatcher.DispatchAsync(bytes, context);

        Assert.True(invoked);
        Assert.Equal(sessionId, sessionContext.SessionId);
    }

    [Fact]
    public async Task HandshakeHandler_AttachesWhenDispatcherRegisteredFirst()
    {
        var runtime = new DefaultClientRuntime();
        var serializer = new JsonNetSerializer();
        var dispatcher = new PacketDispatcher(serializer);
        runtime.RegisterPacketDispatcher(dispatcher);

        var sessionContext = new ClientSessionContext();
        runtime.RegisterSessionContext(sessionContext);

        var sessionId = Guid.NewGuid();
        var envelope = PacketEnvelope<HandshakeResponsePacket>.Create(
            PacketType.HandshakeResponse,
            new HandshakeResponsePacket(true, null, sessionId));

        var bytes = serializer.Serialize(envelope);
        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client);

        await dispatcher.DispatchAsync(bytes, context);

        Assert.Equal(sessionId, sessionContext.SessionId);
    }

    private sealed class TestTransport : INetTransport
    {
#pragma warning disable CS0067 // Events are only here to satisfy the interface for testing
        public event Action<INetConnection>? OnPeerConnected;
        public event Action<INetConnection>? OnPeerDisconnected;
        public event Action<INetConnection, ReadOnlyMemory<byte>, ChannelType>? OnPayloadReceived;
#pragma warning restore CS0067

        public bool IsRunning { get; private set; }
        public TransportStartOptions? LastStartOptions { get; private set; }
        public string? LastShutdownReason { get; private set; }
        private readonly INetConnection _connection = new TestConnection();

        public void Start(TransportStartOptions options)
        {
            LastStartOptions = options;
            IsRunning = true;
        }

        public void Poll(TimeSpan timeout)
        {
            // No-op for tests.
            Thread.Sleep(1);
        }

        public void Shutdown(string? reason = null)
        {
            LastShutdownReason = reason;
            IsRunning = false;
        }

        public void Dispose()
        {
            Shutdown();
        }

        public void RaiseConnected()
        {
            OnPeerConnected?.Invoke(_connection);
        }

        public void RaiseDisconnected()
        {
            OnPeerDisconnected?.Invoke(_connection);
        }

        public void RaisePayload(ReadOnlyMemory<byte> payload, ChannelType channel = ChannelType.ReliableOrdered)
        {
            OnPayloadReceived?.Invoke(_connection, payload, channel);
        }
    }

    private sealed class TestConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint => "test";

        public void Disconnect(string? reason = null) { }

        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered) { }
    }
}
