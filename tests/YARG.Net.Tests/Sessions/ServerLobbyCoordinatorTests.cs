using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Net.Packets;
using YARG.Net.Serialization;
using YARG.Net.Sessions;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Sessions;

public sealed class ServerLobbyCoordinatorTests
{
    [Fact]
    public void HandleHandshakeAccepted_AddsPlayerAndBroadcasts()
    {
        var serializer = new JsonNetSerializer();
        var sessions = new SessionManager();
        var lobby = new LobbyStateManager(sessions);
        using var coordinator = new ServerLobbyCoordinator(sessions, lobby, serializer);

        var connection = new TestConnection();
        Assert.True(sessions.TryCreateSession(connection, "PlayerOne", out var session, out _));

        coordinator.HandleHandshakeAccepted(session!);

        Assert.NotEmpty(connection.SentPayloads);
        var envelope = serializer.Deserialize<PacketEnvelope<LobbyStatePacket>>(connection.SentPayloads.Last().Span);
        Assert.Single(envelope.Payload.Players);
    }

    [Fact]
    public void HandlePeerDisconnected_RemovesPlayer()
    {
        var serializer = new JsonNetSerializer();
        var sessions = new SessionManager();
        var lobby = new LobbyStateManager(sessions);
        using var coordinator = new ServerLobbyCoordinator(sessions, lobby, serializer);

        var hostConnection = new TestConnection();
        Assert.True(sessions.TryCreateSession(hostConnection, "Host", out var hostSession, out _));
        coordinator.HandleHandshakeAccepted(hostSession!);

        var guestConnection = new TestConnection();
        Assert.True(sessions.TryCreateSession(guestConnection, "Guest", out var guestSession, out _));
        coordinator.HandleHandshakeAccepted(guestSession!);

        var hostSendsBefore = hostConnection.SentPayloads.Count;
        coordinator.HandlePeerDisconnected(guestConnection.Id);

        Assert.True(hostConnection.SentPayloads.Count > hostSendsBefore);
        var envelope = serializer.Deserialize<PacketEnvelope<LobbyStatePacket>>(hostConnection.SentPayloads.Last().Span);
        Assert.Single(envelope.Payload.Players);
        Assert.Equal(hostSession.SessionId, envelope.Payload.Players.Single().PlayerId);
    }

    private sealed class TestConnection : INetConnection
    {
        private readonly List<ReadOnlyMemory<byte>> _payloads = new();

        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint { get; } = "test";
        public IReadOnlyList<ReadOnlyMemory<byte>> SentPayloads => _payloads;

        public void Disconnect(string? reason = null)
        {
        }

        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered)
        {
            _payloads.Add(payload.ToArray());
        }
    }
}
