using System;
using System.Linq;
using YARG.Net.Packets;
using YARG.Net.Sessions;
using YARG.Net.Transport;
using Xunit;

namespace YARG.Net.Tests.Sessions;

public sealed class LobbyStateManagerTests
{
    [Fact]
    public void TryAddPlayer_AssignsHostAndBuildsSnapshot()
    {
        var sessionManager = new SessionManager();
        var lobby = new LobbyStateManager(sessionManager);

        var hostSession = CreateSession(sessionManager, "Host");
        Assert.True(lobby.TryAddPlayer(hostSession.SessionId, LobbyRole.Member, out var lobbyPlayer, out var error));
        Assert.Equal(LobbyJoinError.None, error);
        Assert.Equal(LobbyRole.Host, lobbyPlayer!.Role);

        var snapshot = lobby.BuildSnapshot();
        Assert.Single(snapshot.Players);
        Assert.Equal(LobbyStatus.Idle, snapshot.Status);
    }

    [Fact]
    public void TryAddPlayer_RespectsCapacity()
    {
        var sessionManager = new SessionManager();
        var lobby = new LobbyStateManager(sessionManager, new LobbyConfiguration { MaxPlayers = 1 });

        var hostSession = CreateSession(sessionManager, "Host");
        var guestSession = CreateSession(sessionManager, "Guest");

        Assert.True(lobby.TryAddPlayer(hostSession.SessionId, LobbyRole.Member, out _, out _));
        Assert.False(lobby.TryAddPlayer(guestSession.SessionId, LobbyRole.Member, out _, out var error));
        Assert.Equal(LobbyJoinError.LobbyFull, error);
    }

    [Fact]
    public void ReadyTogglesUpdateStatus()
    {
        var sessionManager = new SessionManager();
        var lobby = new LobbyStateManager(sessionManager);

        var hostSession = CreateSession(sessionManager, "Host");
        var guestSession = CreateSession(sessionManager, "Guest");

        lobby.TryAddPlayer(hostSession.SessionId, LobbyRole.Member, out _, out _);
        lobby.TryAddPlayer(guestSession.SessionId, LobbyRole.Member, out _, out _);

        lobby.TryApplySongSelection(new SongSelectionState("song:alpha", Array.Empty<SongInstrumentAssignment>(), false));
        Assert.Equal(LobbyStatus.SelectingSong, lobby.Status);

        lobby.TrySetReady(hostSession.SessionId, true, out _);
        lobby.TrySetReady(guestSession.SessionId, true, out _);
        Assert.Equal(LobbyStatus.ReadyToPlay, lobby.Status);

        lobby.TrySetReady(guestSession.SessionId, false, out _);
        Assert.Equal(LobbyStatus.SelectingSong, lobby.Status);
    }

    [Fact]
    public void SelectingNewSongResetsReadiness()
    {
        var sessionManager = new SessionManager();
        var lobby = new LobbyStateManager(sessionManager);

        var hostSession = CreateSession(sessionManager, "Host");
        var guestSession = CreateSession(sessionManager, "Guest");

        lobby.TryAddPlayer(hostSession.SessionId, LobbyRole.Member, out _, out _);
        lobby.TryAddPlayer(guestSession.SessionId, LobbyRole.Member, out _, out _);

        lobby.TryApplySongSelection(new SongSelectionState("song:alpha", Array.Empty<SongInstrumentAssignment>(), false));
        lobby.TrySetReady(hostSession.SessionId, true, out _);
        lobby.TrySetReady(guestSession.SessionId, true, out _);
        Assert.Equal(LobbyStatus.ReadyToPlay, lobby.Status);

        lobby.TryApplySongSelection(new SongSelectionState("song:beta", Array.Empty<SongInstrumentAssignment>(), false));
        Assert.Equal(LobbyStatus.SelectingSong, lobby.Status);

        var snapshot = lobby.BuildSnapshot();
        Assert.All(snapshot.Players.Where(p => p.Role != LobbyRole.Spectator), player => Assert.False(player.IsReady));
    }

    [Fact]
    public void ApplyingSongSelection_NormalizesAssignments()
    {
        var sessionManager = new SessionManager();
        var lobby = new LobbyStateManager(sessionManager);

        var hostSession = CreateSession(sessionManager, "Host");
        var guestSession = CreateSession(sessionManager, "Guest");
        var spectatorSession = CreateSession(sessionManager, "Viewer");

        lobby.TryAddPlayer(hostSession.SessionId, LobbyRole.Member, out _, out _);
        lobby.TryAddPlayer(guestSession.SessionId, LobbyRole.Member, out _, out _);
        lobby.TryAddPlayer(spectatorSession.SessionId, LobbyRole.Spectator, out _, out _);

        var rawAssignments = new[]
        {
            new SongInstrumentAssignment(hostSession.SessionId, " Guitar ", " Expert "),
            new SongInstrumentAssignment(hostSession.SessionId, "Duplicate", "Medium"),
            new SongInstrumentAssignment(Guid.NewGuid(), "Bass", "Hard"),
            new SongInstrumentAssignment(guestSession.SessionId, "Bass", "Hard"),
            new SongInstrumentAssignment(spectatorSession.SessionId, "Vocals", "Easy"),
            new SongInstrumentAssignment(guestSession.SessionId, "Bass", string.Empty),
        };

        lobby.TryApplySongSelection(new SongSelectionState(" song:alpha ", rawAssignments, true));

        var snapshot = lobby.BuildSnapshot();
        Assert.NotNull(snapshot.Selection);
        var selection = snapshot.Selection!;
        Assert.Equal("song:alpha", selection.SongId);
        Assert.Collection(selection.Assignments,
            assignment =>
            {
                Assert.Equal(hostSession.SessionId, assignment.PlayerId);
                Assert.Equal("Guitar", assignment.Instrument);
                Assert.Equal("Expert", assignment.Difficulty);
            },
            assignment =>
            {
                Assert.Equal(guestSession.SessionId, assignment.PlayerId);
                Assert.Equal("Bass", assignment.Instrument);
                Assert.Equal("Hard", assignment.Difficulty);
            });
    }

    [Fact]
    public void RemovingHostPromotesNextMember()
    {
        var sessionManager = new SessionManager();
        var lobby = new LobbyStateManager(sessionManager);

        var hostSession = CreateSession(sessionManager, "Host");
        var guestSession = CreateSession(sessionManager, "Guest");

        lobby.TryAddPlayer(hostSession.SessionId, LobbyRole.Member, out _, out _);
        lobby.TryAddPlayer(guestSession.SessionId, LobbyRole.Member, out _, out _);

        Assert.True(lobby.TryRemovePlayer(hostSession.SessionId, out _));

        var snapshot = lobby.BuildSnapshot();
        var newHost = Assert.Single(snapshot.Players);
        Assert.Equal(guestSession.SessionId, newHost.PlayerId);
        Assert.Equal(LobbyRole.Host, newHost.Role);
    }

    private static SessionRecord CreateSession(SessionManager sessionManager, string playerName)
    {
        var connection = new TestConnection();
        Assert.True(sessionManager.TryCreateSession(connection, playerName, out var session, out _));
        return session!;
    }

    private sealed class TestConnection : INetConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EndPoint { get; } = "test";
        public void Disconnect(string? reason = null) { }
        public void Send(ReadOnlySpan<byte> payload, ChannelType channel = ChannelType.ReliableOrdered) { }
    }
}
