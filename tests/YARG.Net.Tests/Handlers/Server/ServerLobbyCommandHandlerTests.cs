using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using YARG.Net.Handlers.Server;
using YARG.Net.Packets;
using YARG.Net.Packets.Dispatch;
using YARG.Net.Serialization;
using YARG.Net.Sessions;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Handlers.Server;

public sealed class ServerLobbyCommandHandlerTests
{
    [Fact]
    public async Task ReadyCommand_TogglesPlayer()
    {
        var (handler, sessionManager, lobbyManager) = CreateHandler();
        var serializer = new JsonNetSerializer();
        var dispatcher = new PacketDispatcher(serializer);
        handler.Register(dispatcher);

        var connection = new TestConnection();
        Assert.True(sessionManager.TryCreateSession(connection, "Player", out var session, out _));
        lobbyManager.TryAddPlayer(session!.SessionId, LobbyRole.Member, out _, out _);

        var packet = PacketEnvelope<LobbyReadyStatePacket>.Create(PacketType.LobbyReadyState, new LobbyReadyStatePacket(session.SessionId, true));
        var context = new PacketContext(connection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);
        await dispatcher.DispatchAsync(serializer.Serialize(packet), context);

        Assert.True(lobbyManager.TryGetPlayer(session.SessionId, out var player));
        Assert.True(player!.IsReady);
    }

    [Fact]
    public async Task SongSelection_OnlyHostAllowed()
    {
        var (handler, sessionManager, lobbyManager) = CreateHandler();
        var serializer = new JsonNetSerializer();
        var dispatcher = new PacketDispatcher(serializer);
        handler.Register(dispatcher);

        var hostConnection = new TestConnection();
        var guestConnection = new TestConnection();

        Assert.True(sessionManager.TryCreateSession(hostConnection, "Host", out var hostSession, out _));
        Assert.True(sessionManager.TryCreateSession(guestConnection, "Guest", out var guestSession, out _));

        lobbyManager.TryAddPlayer(hostSession!.SessionId, LobbyRole.Member, out _, out _);
        lobbyManager.TryAddPlayer(guestSession!.SessionId, LobbyRole.Member, out _, out _);

        var hostPacket = PacketEnvelope<SongSelectionPacket>.Create(
            PacketType.SongSelection,
            new SongSelectionPacket(hostSession.SessionId, new SongSelectionState("song:alpha", Array.Empty<SongInstrumentAssignment>(), false)));
        var guestPacket = PacketEnvelope<SongSelectionPacket>.Create(
            PacketType.SongSelection,
            new SongSelectionPacket(guestSession.SessionId, new SongSelectionState("song:beta", Array.Empty<SongInstrumentAssignment>(), false)));

        var hostContext = new PacketContext(hostConnection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);
        var guestContext = new PacketContext(guestConnection, ChannelType.ReliableOrdered, PacketEndpointRole.Server);

        await dispatcher.DispatchAsync(serializer.Serialize(hostPacket), hostContext);
        await dispatcher.DispatchAsync(serializer.Serialize(guestPacket), guestContext);

        Assert.Equal("song:alpha", lobbyManager.SelectedSongId);
    }

    private static (ServerLobbyCommandHandler handler, SessionManager sessions, LobbyStateManager lobby) CreateHandler()
    {
        var sessions = new SessionManager();
        var lobby = new LobbyStateManager(sessions);
        var handler = new ServerLobbyCommandHandler(sessions, lobby);
        return (handler, sessions, lobby);
    }

    private sealed class TestConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint => "server";
        public void Disconnect(string? reason = null) { }
        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered) { }
    }
}
