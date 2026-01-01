using System;
using System.Collections.Generic;
using System.Text;

namespace YARG.Net.Packets;

/// <summary>
/// Sent by the host to synchronize session settings to all clients.
/// </summary>
public sealed record SessionSettingsSyncPacket(
    string LobbyName,
    int MaxPlayers,
    byte PrivacyMode,
    int BandSize,
    bool NoFailMode,
    bool SharedSongsOnly,
    bool AllowModifiers,
    bool EnablePresetSync,
    bool AllowLateJoin,
    List<int> AllowedGameModes,
    bool LocalPlayersFirst) : IPacketPayload;

/// <summary>
/// Binary packet builder/parser for session settings messages.
/// </summary>
public static class SessionSettingsBinaryPackets
{
    /// <summary>
    /// Builds a session settings sync packet.
    /// Format: [PacketType][LobbyNameLength(2)][LobbyName][MaxPlayers(4)][PrivacyMode(1)]
    ///         [BandSize(4)][Flags(1)][GameModeCount(1)][GameModes...]
    /// Flags: bit0=NoFail, bit1=SharedSongs, bit2=AllowMods, bit3=PresetSync, bit4=LateJoin, bit5=LocalPlayersFirst
    /// </summary>
    public static byte[] BuildSessionSettingsSyncPacket(
        string lobbyName,
        int maxPlayers,
        byte privacyMode,
        int bandSize,
        bool noFailMode,
        bool sharedSongsOnly,
        bool allowModifiers,
        bool enablePresetSync,
        bool allowLateJoin,
        List<int> allowedGameModes,
        bool localPlayersFirst)
    {
        var nameBytes = Encoding.UTF8.GetBytes(lobbyName ?? string.Empty);
        var gameModeCount = allowedGameModes?.Count ?? 0;
        
        // Calculate total size:
        // 1 (type) + 2 (name length) + nameBytes.Length + 4 (maxPlayers) + 1 (privacy) 
        // + 4 (bandSize) + 1 (flags) + 1 (gameModeCount) + gameModeCount (each mode is 1 byte)
        int totalSize = 1 + 2 + nameBytes.Length + 4 + 1 + 4 + 1 + 1 + gameModeCount;
        var packet = new byte[totalSize];
        int offset = 0;
        
        // Packet type
        packet[offset++] = (byte)PacketType.SessionPresetSync;
        
        // Lobby name length (2 bytes, big-endian)
        packet[offset++] = (byte)((nameBytes.Length >> 8) & 0xFF);
        packet[offset++] = (byte)(nameBytes.Length & 0xFF);
        
        // Lobby name
        Array.Copy(nameBytes, 0, packet, offset, nameBytes.Length);
        offset += nameBytes.Length;
        
        // Max players (4 bytes, big-endian)
        packet[offset++] = (byte)((maxPlayers >> 24) & 0xFF);
        packet[offset++] = (byte)((maxPlayers >> 16) & 0xFF);
        packet[offset++] = (byte)((maxPlayers >> 8) & 0xFF);
        packet[offset++] = (byte)(maxPlayers & 0xFF);
        
        // Privacy mode (1 byte)
        packet[offset++] = privacyMode;
        
        // Band size (4 bytes, big-endian)
        packet[offset++] = (byte)((bandSize >> 24) & 0xFF);
        packet[offset++] = (byte)((bandSize >> 16) & 0xFF);
        packet[offset++] = (byte)((bandSize >> 8) & 0xFF);
        packet[offset++] = (byte)(bandSize & 0xFF);
        
        // Flags (1 byte)
        byte flags = 0;
        if (noFailMode) flags |= 0x01;
        if (sharedSongsOnly) flags |= 0x02;
        if (allowModifiers) flags |= 0x04;
        if (enablePresetSync) flags |= 0x08;
        if (allowLateJoin) flags |= 0x10;
        if (localPlayersFirst) flags |= 0x20;
        packet[offset++] = flags;
        
        // Game mode count (1 byte)
        packet[offset++] = (byte)gameModeCount;
        
        // Game modes (1 byte each - they're small enum values)
        if (allowedGameModes != null)
        {
            foreach (var mode in allowedGameModes)
            {
                packet[offset++] = (byte)mode;
            }
        }
        
        return packet;
    }

    /// <summary>
    /// Parses a session settings sync packet.
    /// </summary>
    public static bool TryParseSessionSettingsSyncPacket(
        ReadOnlySpan<byte> data,
        out string lobbyName,
        out int maxPlayers,
        out byte privacyMode,
        out int bandSize,
        out bool noFailMode,
        out bool sharedSongsOnly,
        out bool allowModifiers,
        out bool enablePresetSync,
        out bool allowLateJoin,
        out List<int> allowedGameModes,
        out bool localPlayersFirst)
    {
        lobbyName = string.Empty;
        maxPlayers = 4;
        privacyMode = 0;
        bandSize = 0;
        noFailMode = false;
        sharedSongsOnly = true;
        allowModifiers = true;
        enablePresetSync = true;
        allowLateJoin = true;
        allowedGameModes = new List<int>();
        localPlayersFirst = false;
        
        // Minimum size check: type(1) + nameLen(2) + maxPlayers(4) + privacy(1) + bandSize(4) + flags(1) + modeCount(1)
        if (data.Length < 14)
            return false;
        
        int offset = 1; // Skip packet type
        
        // Lobby name length
        int nameLength = (data[offset] << 8) | data[offset + 1];
        offset += 2;
        
        // Check we have enough data for the name
        if (data.Length < offset + nameLength + 11)
            return false;
        
        // Lobby name
        if (nameLength > 0)
        {
            lobbyName = Encoding.UTF8.GetString(data.Slice(offset, nameLength).ToArray());
        }
        offset += nameLength;
        
        // Max players
        maxPlayers = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        offset += 4;
        
        // Privacy mode
        privacyMode = data[offset++];
        
        // Band size
        bandSize = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        offset += 4;
        
        // Flags
        byte flags = data[offset++];
        noFailMode = (flags & 0x01) != 0;
        sharedSongsOnly = (flags & 0x02) != 0;
        allowModifiers = (flags & 0x04) != 0;
        enablePresetSync = (flags & 0x08) != 0;
        allowLateJoin = (flags & 0x10) != 0;
        localPlayersFirst = (flags & 0x20) != 0;
        
        // Game mode count
        int modeCount = data[offset++];
        
        // Check we have enough data for modes
        if (data.Length < offset + modeCount)
            return false;
        
        // Game modes
        allowedGameModes = new List<int>(modeCount);
        for (int i = 0; i < modeCount; i++)
        {
            allowedGameModes.Add(data[offset++]);
        }
        
        return true;
    }
}
