using System;
using System.Threading.Tasks;
using YARG.Net.Handlers.Client;
using YARG.Net.Packets;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Runtime;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Handlers.Client;

public sealed class ClientHandshakeResponseHandlerTests
{
    [Fact]
    public async Task AcceptedHandshake_SetsSessionContext()
    {
        var sessionContext = new ClientSessionContext();
        var handler = new ClientHandshakeResponseHandler(sessionContext);

        var sessionId = Guid.NewGuid();
        var envelope = PacketEnvelope<HandshakeResponsePacket>.Create(
            PacketType.HandshakeResponse,
            new HandshakeResponsePacket(true, null, sessionId));

        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client);
        await handler.HandleAsync(context, envelope, default);

        Assert.Equal(sessionId, sessionContext.SessionId);
        Assert.True(sessionContext.HasSession);
    }

    [Fact]
    public async Task RejectedHandshake_ClearsSessionContext()
    {
        var sessionContext = new ClientSessionContext();
        var handler = new ClientHandshakeResponseHandler(sessionContext);

        var previous = Guid.NewGuid();
        sessionContext.TrySetSession(previous);

        var envelope = PacketEnvelope<HandshakeResponsePacket>.Create(
            PacketType.HandshakeResponse,
            new HandshakeResponsePacket(false, "bad", Guid.Empty));

        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client);
        await handler.HandleAsync(context, envelope, default);

        Assert.False(sessionContext.HasSession);
        Assert.Null(sessionContext.SessionId);
    }

    private sealed class TestConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint => "client";
        public void Disconnect(string? reason = null) { }
        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered) { }
    }
}
