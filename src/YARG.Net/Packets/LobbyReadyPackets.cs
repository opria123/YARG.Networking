using System;

namespace YARG.Net.Packets;

public sealed record LobbyReadyStatePacket(Guid SessionId, bool IsReady) : IPacketPayload;

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for ready-state-related messages.
/// </summary>
public static class ReadyStateBinaryPackets
{
    /// <summary>
    /// Builds a ready state packet (legacy format without instrument/difficulty).
    /// </summary>
    public static byte[] BuildReadyStatePacket(string playerName, bool isReady)
    {
        int size = 1 + PacketWriter.GetStringSize(playerName) + 1; // Type + name + bool
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.LobbyReadyState);
        writer.WriteString(playerName);
        writer.WriteBool(isReady);
        
        return buffer;
    }

    /// <summary>
    /// Builds a client ready state packet with instrument, difficulty, and sitting out state.
    /// Format: [PacketType (1)][isReady (1)][nameLen (2)][name][instrument (1)][difficulty (1)][sittingOut (1)]
    /// </summary>
    public static byte[] BuildClientReadyPacket(string playerName, bool isReady, int instrument, int difficulty, bool sittingOut = false)
    {
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(playerName);
        byte[] message = new byte[1 + 1 + 2 + nameBytes.Length + 3]; // +1 for sittingOut
        
        message[0] = (byte)PacketType.LobbyReadyState;
        message[1] = isReady ? (byte)1 : (byte)0;
        message[2] = (byte)(nameBytes.Length >> 8);
        message[3] = (byte)(nameBytes.Length & 0xFF);
        Array.Copy(nameBytes, 0, message, 4, nameBytes.Length);
        message[4 + nameBytes.Length] = (byte)instrument;
        message[4 + nameBytes.Length + 1] = (byte)difficulty;
        message[4 + nameBytes.Length + 2] = sittingOut ? (byte)1 : (byte)0;
        
        return message;
    }

    /// <summary>
    /// Builds a host broadcast ready state packet with isLocalPlayer flag, instrument/difficulty, sitting out state, and NetworkPlayerId.
    /// Format: [PacketType (1)][isReady (1)][nameLen (2)][name][isLocalPlayer (1)][instrument (1)][difficulty (1)][sittingOut (1)][networkPlayerId (16)]
    /// </summary>
    public static byte[] BuildHostBroadcastPacket(string playerName, bool isReady, bool isLocalPlayer, int instrument, int difficulty, bool sittingOut = false, Guid networkPlayerId = default)
    {
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(playerName);
        byte[] message = new byte[1 + 1 + 2 + nameBytes.Length + 4 + 16]; // +16 for NetworkPlayerId GUID
        
        message[0] = (byte)PacketType.LobbyReadyState;
        message[1] = isReady ? (byte)1 : (byte)0;
        message[2] = (byte)(nameBytes.Length >> 8);
        message[3] = (byte)(nameBytes.Length & 0xFF);
        Array.Copy(nameBytes, 0, message, 4, nameBytes.Length);
        message[4 + nameBytes.Length] = isLocalPlayer ? (byte)1 : (byte)0;
        message[4 + nameBytes.Length + 1] = (byte)instrument;
        message[4 + nameBytes.Length + 2] = (byte)difficulty;
        message[4 + nameBytes.Length + 3] = sittingOut ? (byte)1 : (byte)0;
        
        // Write NetworkPlayerId as 16 bytes
        byte[] guidBytes = networkPlayerId.ToByteArray();
        Array.Copy(guidBytes, 0, message, 4 + nameBytes.Length + 4, 16);
        
        return message;
    }

    /// <summary>
    /// Builds an all-players-ready packet.
    /// </summary>
    public static byte[] BuildAllPlayersReadyPacket()
    {
        return new byte[] { (byte)PacketType.AllPlayersReady };
    }

    /// <summary>
    /// Parsed ready state data (legacy format).
    /// </summary>
    public readonly struct ParsedReadyState
    {
        public string PlayerName { get; init; }
        public bool IsReady { get; init; }
    }

    /// <summary>
    /// Parsed client ready state data with instrument/difficulty and sitting out state.
    /// </summary>
    public readonly struct ParsedClientReadyState
    {
        public bool IsValid { get; init; }
        public string PlayerName { get; init; }
        public bool IsReady { get; init; }
        public int Instrument { get; init; }
        public int Difficulty { get; init; }
        public bool HasInstrumentData { get; init; }
        public bool SittingOut { get; init; }
    }

    /// <summary>
    /// Parsed host broadcast ready state data with sitting out state and NetworkPlayerId.
    /// </summary>
    public readonly struct ParsedHostBroadcast
    {
        public bool IsValid { get; init; }
        public string PlayerName { get; init; }
        public bool IsReady { get; init; }
        public bool IsLocalPlayer { get; init; }
        public int Instrument { get; init; }
        public int Difficulty { get; init; }
        public bool SittingOut { get; init; }
        public Guid NetworkPlayerId { get; init; }
    }

    /// <summary>
    /// Parses a ready state packet (legacy format).
    /// </summary>
    public static bool TryParseReadyStatePacket(ReadOnlySpan<byte> data, out ParsedReadyState result)
    {
        result = default;
        
        if (data.Length < 4) // Type + min name + bool
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            result = new ParsedReadyState
            {
                PlayerName = reader.ReadString(),
                IsReady = reader.ReadBool()
            };
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a client ready state packet with instrument/difficulty and sitting out state.
    /// </summary>
    public static ParsedClientReadyState ParseClientReadyPacket(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return new ParsedClientReadyState { IsValid = false };
        
        bool isReady = data[1] == 1;
        int nameLen = (data[2] << 8) | data[3];
        
        if (data.Length < 4 + nameLen)
            return new ParsedClientReadyState { IsValid = false };
        
        string playerName = System.Text.Encoding.UTF8.GetString(data.Slice(4, nameLen));
        
        // Parse instrument, difficulty, and sitting out if present
        int instrument = 0;
        int difficulty = 0;
        bool sittingOut = false;
        bool hasInstrumentData = false;
        
        if (data.Length >= 4 + nameLen + 2)
        {
            instrument = data[4 + nameLen];
            difficulty = data[4 + nameLen + 1];
            hasInstrumentData = true;
            
            // Parse sitting out if present (new field)
            if (data.Length >= 4 + nameLen + 3)
            {
                sittingOut = data[4 + nameLen + 2] == 1;
            }
        }
        
        return new ParsedClientReadyState
        {
            IsValid = true,
            PlayerName = playerName,
            IsReady = isReady,
            Instrument = instrument,
            Difficulty = difficulty,
            HasInstrumentData = hasInstrumentData,
            SittingOut = sittingOut
        };
    }

    /// <summary>
    /// Parses a host broadcast ready state packet with sitting out state and NetworkPlayerId.
    /// </summary>
    public static ParsedHostBroadcast ParseHostBroadcastPacket(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return new ParsedHostBroadcast { IsValid = false };
        
        bool isReady = data[1] == 1;
        int nameLen = (data[2] << 8) | data[3];
        
        if (data.Length < 4 + nameLen)
            return new ParsedHostBroadcast { IsValid = false };
        
        string playerName = System.Text.Encoding.UTF8.GetString(data.Slice(4, nameLen));
        
        bool isLocalPlayer = false;
        int instrument = 0;
        int difficulty = 0;
        bool sittingOut = false;
        Guid networkPlayerId = Guid.Empty;
        
        int extraOffset = 4 + nameLen;
        if (data.Length >= extraOffset + 4 + 16)
        {
            // New format with NetworkPlayerId (16 bytes)
            isLocalPlayer = data[extraOffset] == 1;
            instrument = data[extraOffset + 1];
            difficulty = data[extraOffset + 2];
            sittingOut = data[extraOffset + 3] == 1;
            
            // Read NetworkPlayerId GUID
            byte[] guidBytes = data.Slice(extraOffset + 4, 16).ToArray();
            networkPlayerId = new Guid(guidBytes);
        }
        else if (data.Length >= extraOffset + 4)
        {
            // Legacy format with sitting out but no NetworkPlayerId
            isLocalPlayer = data[extraOffset] == 1;
            instrument = data[extraOffset + 1];
            difficulty = data[extraOffset + 2];
            sittingOut = data[extraOffset + 3] == 1;
        }
        else if (data.Length >= extraOffset + 3)
        {
            // Format with instrument data but no sitting out (legacy)
            isLocalPlayer = data[extraOffset] == 1;
            instrument = data[extraOffset + 1];
            difficulty = data[extraOffset + 2];
        }
        else if (data.Length >= extraOffset + 1)
        {
            // Legacy format without instrument data
            isLocalPlayer = data[extraOffset] == 1;
        }
        
        return new ParsedHostBroadcast
        {
            IsValid = true,
            PlayerName = playerName,
            IsReady = isReady,
            IsLocalPlayer = isLocalPlayer,
            Instrument = instrument,
            Difficulty = difficulty,
            SittingOut = sittingOut,
            NetworkPlayerId = networkPlayerId
        };
    }
}

#endregion
