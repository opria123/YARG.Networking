using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using YARG.Net.Handlers;
using YARG.Net.Packets;
using YARG.Net.Serialization;
using YARG.Net.Sessions;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Handlers;

public sealed class ServerHandshakeHandlerTests
{
    [Fact]
    public async Task HandleAsync_AcceptsValidHandshake()
    {
        var serializer = new JsonNetSerializer();
        var sessions = new SessionManager();
        var handler = new ServerHandshakeHandler(sessions, serializer);

        SessionRecord? acceptedSession = null;
        handler.HandshakeAccepted += (_, session) => acceptedSession = session;

        var connection = new TestConnection();
        var request = PacketEnvelope<HandshakeRequestPacket>.Create(
            PacketType.HandshakeRequest,
            new HandshakeRequestPacket(ProtocolVersion.Current, "PlayerOne", null));

        var context = new PacketContext(connection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);
        await handler.HandleAsync(context, request, default);

        Assert.NotNull(acceptedSession);
        Assert.Equal(connection.Id, acceptedSession!.ConnectionId);
        Assert.Equal("PlayerOne", acceptedSession.PlayerName);
        Assert.Single(connection.SentPayloads);

        var response = serializer.Deserialize<PacketEnvelope<HandshakeResponsePacket>>(connection.SentPayloads.Single().Span);
        Assert.True(response.Payload.Accepted);
        Assert.Null(response.Payload.Reason);
        Assert.NotEqual(Guid.Empty, response.Payload.SessionId);
        Assert.False(connection.WasDisconnected);
        Assert.Equal(ChannelType.ReliableOrdered, connection.LastChannel);
    }

    [Fact]
    public async Task HandleAsync_RejectsProtocolMismatch()
    {
        var serializer = new JsonNetSerializer();
        var handler = new ServerHandshakeHandler(new SessionManager(), serializer);
        var connection = new TestConnection();

        var request = PacketEnvelope<HandshakeRequestPacket>.Create(
            PacketType.HandshakeRequest,
            new HandshakeRequestPacket("0.0.1", "PlayerOne"));

        var context = new PacketContext(connection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);
        await handler.HandleAsync(context, request, default);

        var response = serializer.Deserialize<PacketEnvelope<HandshakeResponsePacket>>(connection.SentPayloads.Single().Span);
        Assert.False(response.Payload.Accepted);
        Assert.Contains("Protocol mismatch", response.Payload.Reason);
        Assert.Equal(Guid.Empty, response.Payload.SessionId);
        Assert.True(connection.WasDisconnected);
    }

    [Fact]
    public async Task HandleAsync_RejectsDuplicateHandshake()
    {
        var serializer = new JsonNetSerializer();
        var handler = new ServerHandshakeHandler(new SessionManager(), serializer);
        var connection = new TestConnection();
        var context = new PacketContext(connection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);

        var request = PacketEnvelope<HandshakeRequestPacket>.Create(
            PacketType.HandshakeRequest,
            new HandshakeRequestPacket(ProtocolVersion.Current, "PlayerOne"));

        await handler.HandleAsync(context, request, default);
        await handler.HandleAsync(context, request, default);

        Assert.Equal(2, connection.SentPayloads.Count);
        var response = serializer.Deserialize<PacketEnvelope<HandshakeResponsePacket>>(connection.SentPayloads.Last().Span);
        Assert.False(response.Payload.Accepted);
        Assert.Contains("already", response.Payload.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_RejectsWhenServerFull()
    {
        var serializer = new JsonNetSerializer();
        var handler = new ServerHandshakeHandler(new SessionManager(capacity: 1), serializer);

        var firstConnection = new TestConnection();
        var firstRequest = PacketEnvelope<HandshakeRequestPacket>.Create(
            PacketType.HandshakeRequest,
            new HandshakeRequestPacket(ProtocolVersion.Current, "Host"));
        var firstContext = new PacketContext(firstConnection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);
        await handler.HandleAsync(firstContext, firstRequest, default);

        var secondConnection = new TestConnection();
        var secondRequest = PacketEnvelope<HandshakeRequestPacket>.Create(
            PacketType.HandshakeRequest,
            new HandshakeRequestPacket(ProtocolVersion.Current, "Guest"));
        var secondContext = new PacketContext(secondConnection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);
        await handler.HandleAsync(secondContext, secondRequest, default);

        var response = serializer.Deserialize<PacketEnvelope<HandshakeResponsePacket>>(secondConnection.SentPayloads.Single().Span);
        Assert.False(response.Payload.Accepted);
        Assert.Equal("Server is full.", response.Payload.Reason);
    }

    [Fact]
    public async Task HandleAsync_RejectsInvalidPassword()
    {
        var serializer = new JsonNetSerializer();
        var handler = new ServerHandshakeHandler(
            new SessionManager(),
            serializer,
            new HandshakeServerOptions { Password = "secret" });

        var connection = new TestConnection();
        var request = PacketEnvelope<HandshakeRequestPacket>.Create(
            PacketType.HandshakeRequest,
            new HandshakeRequestPacket(ProtocolVersion.Current, "PlayerOne", "wrong"));

        var context = new PacketContext(connection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);
        await handler.HandleAsync(context, request, default);

        var response = serializer.Deserialize<PacketEnvelope<HandshakeResponsePacket>>(connection.SentPayloads.Single().Span);
        Assert.False(response.Payload.Accepted);
        Assert.Equal("Invalid password.", response.Payload.Reason);
        Assert.True(connection.WasDisconnected);
    }

    private sealed class TestConnection : INetConnection
    {
        private readonly List<ReadOnlyMemory<byte>> _payloads = new();

        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint { get; } = "test";
        public bool WasDisconnected { get; private set; }
        public string? DisconnectReason { get; private set; }
        public ChannelType? LastChannel { get; private set; }
        public IReadOnlyList<ReadOnlyMemory<byte>> SentPayloads => _payloads;

        public void Disconnect(string? reason = null)
        {
            WasDisconnected = true;
            DisconnectReason = reason;
        }

        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered)
        {
            _payloads.Add(payload.ToArray());
            LastChannel = channel;
        }
    }
}
