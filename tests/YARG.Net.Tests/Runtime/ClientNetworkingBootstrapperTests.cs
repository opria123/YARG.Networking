using System;
using System.Threading.Tasks;
using YARG.Net.Packets;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Runtime;
using YARG.Net.Serialization;
using YARG.Net.Transport;
using YARG.Net.Handlers.Client;
using Xunit;

namespace YARG.Net.Tests.Runtime;

public sealed class ClientNetworkingBootstrapperTests
{
    [Fact]
    public async Task Initialize_WiresSessionAndHandlers()
    {
        using var transport = new NoopTransport();
        var serializer = new JsonNetSerializer();
        var services = ClientNetworkingBootstrapper.Initialize(transport, serializer);

        Assert.NotNull(services.Runtime);
        Assert.NotNull(services.SessionContext);
        Assert.Same(services.SessionContext, services.Runtime.SessionContext);

        var handshakeEnvelope = PacketEnvelope<HandshakeResponsePacket>.Create(
            PacketType.HandshakeResponse,
            new HandshakeResponsePacket(true, null, Guid.NewGuid()));

        await services.PacketDispatcher.DispatchAsync(
            serializer.Serialize(handshakeEnvelope),
            new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client));

        Assert.True(services.SessionContext.HasSession);

        var lobbyStateEnvelope = PacketEnvelope<LobbyStatePacket>.Create(
            PacketType.LobbyState,
            new LobbyStatePacket(Guid.NewGuid(), Array.Empty<LobbyPlayer>(), LobbyStatus.Idle, null));

        var invoked = false;
        services.LobbyStateHandler.LobbyStateChanged += (_, _) => invoked = true;

        await services.PacketDispatcher.DispatchAsync(
            serializer.Serialize(lobbyStateEnvelope),
            new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client));

        Assert.True(invoked);
    }

    private sealed class NoopTransport : INetTransport
    {
#pragma warning disable CS0067
        public event Action<INetConnection>? OnPeerConnected;
        public event Action<INetConnection>? OnPeerDisconnected;
        public event Action<INetConnection, ReadOnlyMemory<byte>, ChannelType>? OnPayloadReceived;
#pragma warning restore CS0067

        public bool IsRunning => false;

        public void Dispose() { }
        public void Poll(TimeSpan timeout) { }
        public void Shutdown(string? reason = null) { }

        public void Start(TransportStartOptions options)
        {
        }
    }

    private sealed class TestConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint => "bootstrap";
        public void Disconnect(string? reason = null) { }
        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered) { }
    }
}
