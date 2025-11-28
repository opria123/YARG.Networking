using System;
using System.Collections.Generic;
using YARG.Net.Handlers.Client;
using YARG.Net.Packets;
using YARG.Net.Runtime;
using YARG.Net.Serialization;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Handlers.Client;

public sealed class ClientLobbyCommandSenderTests
{
    [Fact]
    public void SendReadyState_WritesPacket()
    {
        var serializer = new JsonNetSerializer();
        var sender = new ClientLobbyCommandSender(serializer);
        var connection = new RecordingConnection();
        var sessionId = Guid.NewGuid();

        sender.SendReadyState(connection, sessionId, true);

        var envelope = serializer.Deserialize<PacketEnvelope<LobbyReadyStatePacket>>(connection.LastPayload!.Value.Span);
        Assert.Equal(PacketType.LobbyReadyState, envelope.Type);
        Assert.Equal(sessionId, envelope.Payload.SessionId);
        Assert.True(envelope.Payload.IsReady);
    }

    [Fact]
    public void SendSongSelection_WritesPacket()
    {
        var serializer = new JsonNetSerializer();
        var sender = new ClientLobbyCommandSender(serializer);
        var connection = new RecordingConnection();
        var sessionId = Guid.NewGuid();

        var assignments = new List<SongInstrumentAssignment>
        {
            new(sessionId, "Guitar", "Expert"),
        };

        var state = new SongSelectionState("song:abc", assignments, false);
        sender.SendSongSelection(connection, sessionId, state);

        var envelope = serializer.Deserialize<PacketEnvelope<SongSelectionPacket>>(connection.LastPayload!.Value.Span);
        Assert.Equal(PacketType.SongSelection, envelope.Type);
        Assert.Equal(sessionId, envelope.Payload.SessionId);
        Assert.Equal(state.SongId, envelope.Payload.State.SongId);
        Assert.Equal(state.Assignments, envelope.Payload.State.Assignments);
        Assert.Equal(state.AllReady, envelope.Payload.State.AllReady);
    }

    [Fact]
    public void SendReadyState_UsesSessionContext()
    {
        var serializer = new JsonNetSerializer();
        var sender = new ClientLobbyCommandSender(serializer);
        var connection = new RecordingConnection();
        var sessionContext = new ClientSessionContext();
        var sessionId = Guid.NewGuid();
        sessionContext.TrySetSession(sessionId);

        sender.SendReadyState(connection, sessionContext, true);

        var envelope = serializer.Deserialize<PacketEnvelope<LobbyReadyStatePacket>>(connection.LastPayload!.Value.Span);
        Assert.Equal(sessionId, envelope.Payload.SessionId);
    }

    [Fact]
    public void SendSongSelection_UsesSessionContext()
    {
        var serializer = new JsonNetSerializer();
        var sender = new ClientLobbyCommandSender(serializer);
        var connection = new RecordingConnection();
        var sessionContext = new ClientSessionContext();
        var sessionId = Guid.NewGuid();
        sessionContext.TrySetSession(sessionId);

        var state = new SongSelectionState("song:abc", new List<SongInstrumentAssignment>(), false);
        sender.SendSongSelection(connection, sessionContext, state);

        var envelope = serializer.Deserialize<PacketEnvelope<SongSelectionPacket>>(connection.LastPayload!.Value.Span);
        Assert.Equal(sessionId, envelope.Payload.SessionId);
    }

    [Fact]
    public void MissingSessionContext_Throws()
    {
        var serializer = new JsonNetSerializer();
        var sender = new ClientLobbyCommandSender(serializer);
        var connection = new RecordingConnection();
        var sessionContext = new ClientSessionContext();

        Assert.Throws<InvalidOperationException>(() => sender.SendReadyState(connection, sessionContext, true));
        Assert.Throws<InvalidOperationException>(() => sender.SendSongSelection(connection, sessionContext, new SongSelectionState("song", Array.Empty<SongInstrumentAssignment>(), false)));
    }

    private sealed class RecordingConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint => "client";
        public ReadOnlyMemory<byte>? LastPayload { get; private set; }

        public void Disconnect(string? reason = null)
        {
        }

        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered)
        {
            LastPayload = payload.ToArray();
        }
    }
}
