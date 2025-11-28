using System;
using YARG.Net.Packets;
using YARG.Net.Serialization;
using YARG.Net.Tests.Packets.Samples;
using Xunit;

namespace YARG.Net.Tests.Packets;

public sealed class PacketEnvelopeTests
{
    [Fact]
    public void EnvelopeRoundTrip_PreservesPayload()
    {
        var payload = new HandshakeRequestPacket("0.1.0", "PlayerOne", "password");
        var envelope = PacketEnvelope<HandshakeRequestPacket>.Create(PacketType.HandshakeRequest, payload);

        var serializer = new JsonNetSerializer();
        var bytes = serializer.Serialize(envelope);
        var clone = serializer.Deserialize<PacketEnvelope<HandshakeRequestPacket>>(bytes.Span);

        Assert.Equal(envelope, clone);
        Assert.Equal(PacketType.HandshakeRequest, clone.Type);
        Assert.Equal(payload, clone.Payload);
        Assert.Equal(ProtocolVersion.Current, clone.Version);
    }

    [Fact]
    public void DifferentPayloadTypes_SerializeIndependently()
    {
        var heartbeat = PacketEnvelope<HeartbeatPacket>.Create(PacketType.Heartbeat, new HeartbeatPacket(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        var serializer = new JsonNetSerializer();

        var bytes = serializer.Serialize(heartbeat);
        var roundTrip = serializer.Deserialize<PacketEnvelope<HeartbeatPacket>>(bytes.Span);

        Assert.Equal(heartbeat, roundTrip);
    }

    [Fact]
    public void LobbyStatePacket_RoundTrips()
    {
        AssertRoundTrip(PacketSampleFactory.CreateLobbyState());
    }

    [Fact]
    public void LobbyInvitePacket_RoundTrips()
    {
        AssertRoundTrip(PacketSampleFactory.CreateLobbyInvite());
    }

    [Fact]
    public void SongSelectionPacket_RoundTrips()
    {
        AssertRoundTrip(PacketSampleFactory.CreateSongSelection());
    }

    [Fact]
    public void GameplayCountdownPacket_RoundTrips()
    {
        AssertRoundTrip(PacketSampleFactory.CreateCountdown());
    }

    [Fact]
    public void GameplayInputFramePacket_RoundTrips()
    {
        AssertRoundTrip(PacketSampleFactory.CreateInputFrame());
    }

    private static void AssertRoundTrip<T>(PacketEnvelope<T> envelope)
        where T : IPacketPayload
    {
        var serializer = new JsonNetSerializer();
        var bytes = serializer.Serialize(envelope);
        var clone = serializer.Deserialize<PacketEnvelope<T>>(bytes.Span);

        Assert.Equal(envelope.Type, clone.Type);
        Assert.Equal(envelope.Version, clone.Version);

        var originalBytes = serializer.Serialize(envelope);
        var cloneBytes = serializer.Serialize(clone);
        Assert.True(originalBytes.Span.SequenceEqual(cloneBytes.Span), "Serialized payload mismatch after round-trip.");
    }
}
