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
    /// Builds a ready state packet.
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
    /// Builds an all-players-ready packet.
    /// </summary>
    public static byte[] BuildAllPlayersReadyPacket()
    {
        return new byte[] { (byte)PacketType.AllPlayersReady };
    }

    /// <summary>
    /// Parsed ready state data.
    /// </summary>
    public readonly struct ParsedReadyState
    {
        public string PlayerName { get; init; }
        public bool IsReady { get; init; }
    }

    /// <summary>
    /// Parses a ready state packet.
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
}

#endregion
