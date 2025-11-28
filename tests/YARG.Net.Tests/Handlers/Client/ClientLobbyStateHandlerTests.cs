using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using YARG.Net.Handlers.Client;
using YARG.Net.Packets;
using YARG.Net.Sessions;
using YARG.Net.Tests.Packets.Samples;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Handlers.Client;

public sealed class ClientLobbyStateHandlerTests
{
    [Fact]
    public async Task HandleAsync_CachesSnapshotAndRaisesEvent()
    {
        var handler = new ClientLobbyStateHandler();
        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client);
        var envelope = PacketSampleFactory.CreateLobbyState();

        LobbyStateSnapshot? snapshot = null;
        handler.LobbyStateChanged += (_, args) => snapshot = args.Snapshot;

        await handler.HandleAsync(context, envelope, default);

        Assert.NotNull(snapshot);
        Assert.True(handler.TryGetSnapshot(out var cached));
        Assert.Equal(snapshot, cached);
    }

    [Fact]
    public async Task HandleAsync_IgnoresNonClientRoles()
    {
        var handler = new ClientLobbyStateHandler();
        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Server);
        var envelope = PacketSampleFactory.CreateLobbyState();

        var invoked = false;
        handler.LobbyStateChanged += (_, _) => invoked = true;

        await handler.HandleAsync(context, envelope, default);

        Assert.False(invoked);
        Assert.False(handler.TryGetSnapshot(out _));
    }

    [Fact]
    public async Task DuplicateSnapshots_DoNotRaiseEvent()
    {
        var handler = new ClientLobbyStateHandler();
        var context = new PacketContext(new TestConnection(), ChannelType.ReliableOrdered, PacketEndpointRole.Client);

        var players = new List<LobbyPlayer>
        {
            new(Guid.NewGuid(), "Host", LobbyRole.Host, true),
            new(Guid.NewGuid(), "Guest", LobbyRole.Member, false),
        };

        var assignments = new List<SongInstrumentAssignment>
        {
            new(players[0].PlayerId, "Guitar", "Expert"),
            new(players[1].PlayerId, "Bass", "Hard"),
        };

        var selection = new SongSelectionState("song:abc", assignments, false);
        var payload = new LobbyStatePacket(Guid.NewGuid(), players, LobbyStatus.SelectingSong, selection);
        var envelope = PacketEnvelope<LobbyStatePacket>.Create(PacketType.LobbyState, payload);

        var invocationCount = 0;
        handler.LobbyStateChanged += (_, _) => invocationCount++;

        await handler.HandleAsync(context, envelope, default);
        await handler.HandleAsync(context, envelope, default);

        Assert.Equal(1, invocationCount);
    }

    private sealed class TestConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint => "test";
        public void Disconnect(string? reason = null) { }
        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered) { }
    }
}
