using System;
using System.Collections.Generic;
using YARG.Net.Packets;

namespace YARG.Net.Tests.Packets.Samples;

internal static class PacketSampleFactory
{
    public static PacketEnvelope<LobbyStatePacket> CreateLobbyState()
    {
        var players = new List<LobbyPlayer>
        {
            new(Guid.NewGuid(), "Host", LobbyRole.Host, true),
            new(Guid.NewGuid(), "Guitar", LobbyRole.Member, false),
        };

        var selection = new SongSelectionState("song:abc", Array.Empty<SongInstrumentAssignment>(), false);
        var payload = new LobbyStatePacket(Guid.NewGuid(), players, LobbyStatus.SelectingSong, selection);
        return PacketEnvelope<LobbyStatePacket>.Create(PacketType.LobbyState, payload);
    }

    public static PacketEnvelope<LobbyInvitePacket> CreateLobbyInvite()
    {
        var inviter = new LobbyPlayer(Guid.NewGuid(), "Host", LobbyRole.Host, true);
        var payload = new LobbyInvitePacket(Guid.NewGuid(), inviter, "INV123");
        return PacketEnvelope<LobbyInvitePacket>.Create(PacketType.LobbyInvite, payload);
    }

    public static PacketEnvelope<SongSelectionPacket> CreateSongSelection()
    {
        var assignments = new List<SongInstrumentAssignment>
        {
            new(Guid.NewGuid(), "Guitar", "Expert"),
            new(Guid.NewGuid(), "Drums", "Hard"),
        };

        var payload = new SongSelectionPacket(Guid.NewGuid(), new SongSelectionState("song:def", assignments, true));
        return PacketEnvelope<SongSelectionPacket>.Create(PacketType.SongSelection, payload);
    }

    public static PacketEnvelope<GameplayCountdownPacket> CreateCountdown()
    {
        var payload = new GameplayCountdownPacket(Guid.NewGuid(), 5);
        return PacketEnvelope<GameplayCountdownPacket>.Create(PacketType.GameplayCountdown, payload);
    }

    public static PacketEnvelope<GameplayInputFramePacket> CreateInputFrame()
    {
        var inputs = new List<InputEvent>
        {
            new(Guid.NewGuid(), "Green", 1.0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        };

        var payload = new GameplayInputFramePacket(Guid.NewGuid(), 12345, inputs);
        return PacketEnvelope<GameplayInputFramePacket>.Create(PacketType.GameplayInputFrame, payload);
    }
}
