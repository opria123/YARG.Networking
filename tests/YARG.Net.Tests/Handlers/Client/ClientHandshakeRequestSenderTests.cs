using System;
using YARG.Net.Handlers.Client;
using YARG.Net.Packets;
using YARG.Net.Serialization;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Handlers.Client;

public sealed class ClientHandshakeRequestSenderTests
{
    [Fact]
    public void SendHandshake_UsesProvidedMetadata()
    {
        var serializer = new JsonNetSerializer();
        var sender = new ClientHandshakeRequestSender(serializer);
        var connection = new RecordingConnection();

        sender.SendHandshake(connection, "1.2.3", "Player One", "secret");

        Assert.NotNull(connection.LastPayload);
        var envelope = serializer.Deserialize<PacketEnvelope<HandshakeRequestPacket>>(connection.LastPayload!.Value.Span);
        Assert.Equal(PacketType.HandshakeRequest, envelope.Type);
        Assert.Equal("1.2.3", envelope.Payload.ClientVersion);
        Assert.Equal("Player One", envelope.Payload.PlayerName);
        Assert.Equal("secret", envelope.Payload.Password);
    }

    [Fact]
    public void SendHandshake_DefaultVersion_UsesProtocolConstant()
    {
        var serializer = new JsonNetSerializer();
        var sender = new ClientHandshakeRequestSender(serializer);
        var connection = new RecordingConnection();

        sender.SendHandshake(connection, "Player Two");

        var envelope = serializer.Deserialize<PacketEnvelope<HandshakeRequestPacket>>(connection.LastPayload!.Value.Span);
        Assert.Equal(ProtocolVersion.Current, envelope.Payload.ClientVersion);
        Assert.Equal("Player Two", envelope.Payload.PlayerName);
    }

    private sealed class RecordingConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint => "test";
        public ReadOnlyMemory<byte>? LastPayload { get; private set; }
        public ChannelType? LastChannel { get; private set; }

        public void Disconnect(string? reason = null)
        {
        }

        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered)
        {
            var buffer = new byte[payload.Length];
            payload.CopyTo(buffer);
            LastPayload = buffer;
            LastChannel = channel;
        }
    }
}
