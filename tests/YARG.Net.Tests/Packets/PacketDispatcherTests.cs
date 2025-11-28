using System;
using System.Threading;
using System.Threading.Tasks;
using YARG.Net.Packets;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Serialization;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Packets;

public sealed class PacketDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_InvokesRegisteredHandler()
    {
        var serializer = new JsonNetSerializer();
        var dispatcher = new PacketDispatcher(serializer);

        var handled = false;
        dispatcher.RegisterHandler<HeartbeatPacket>(PacketType.Heartbeat, (context, envelope, _) =>
        {
            handled = context.Role == PacketEndpointRole.Server && envelope.Payload.TimestampUnixMs > 0;
            return Task.CompletedTask;
        });

        var envelope = PacketEnvelope<HeartbeatPacket>.Create(PacketType.Heartbeat, new HeartbeatPacket(1234));
        var bytes = serializer.Serialize(envelope);
        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Server);

        var result = await dispatcher.DispatchAsync(bytes, context);

        Assert.True(result);
        Assert.True(handled);
    }

    [Fact]
    public async Task DispatchAsync_UnknownPacket_ReturnsFalse()
    {
        var serializer = new JsonNetSerializer();
        var dispatcher = new PacketDispatcher(serializer);

        var envelope = PacketEnvelope<HeartbeatPacket>.Create(PacketType.Heartbeat, new HeartbeatPacket(1234));
        var bytes = serializer.Serialize(envelope);
        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client);

        var result = await dispatcher.DispatchAsync(bytes, context);
        Assert.False(result);
    }

    private sealed class TestConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint => "test";
        public void Disconnect(string? reason = null) { }
        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered) { }
    }
}
