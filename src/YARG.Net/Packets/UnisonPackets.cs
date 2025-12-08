using System;

namespace YARG.Net.Packets;

/// <summary>
/// Sent by a player when they successfully complete a unison star power phrase.
/// </summary>
public sealed record UnisonPhraseHitPacket(
    Guid SessionId,
    string PlayerName,
    double PhraseTime,
    double PhraseEndTime) : IPacketPayload;

/// <summary>
/// Sent by the host when all players have completed a unison phrase
/// and the bonus should be awarded.
/// </summary>
public sealed record UnisonBonusAwardPacket(
    Guid LobbyId,
    double PhraseTime) : IPacketPayload;

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for unison-related messages.
/// </summary>
public static class UnisonBinaryPackets
{
    /// <summary>
    /// Builds a unison phrase hit packet.
    /// </summary>
    public static byte[] BuildPhraseHitPacket(string playerName, double phraseStartTime, double phraseEndTime)
    {
        int size = 1 + PacketWriter.GetStringSize(playerName) + 16; // Type + name + 2 doubles
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.UnisonPhraseHit);
        writer.WriteString(playerName);
        writer.WriteDouble(phraseStartTime);
        writer.WriteDouble(phraseEndTime);
        
        return buffer;
    }

    /// <summary>
    /// Builds a unison bonus award packet.
    /// </summary>
    public static byte[] BuildBonusAwardPacket(double phraseStartTime)
    {
        byte[] buffer = new byte[9]; // Type + double
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.UnisonBonusAward);
        writer.WriteDouble(phraseStartTime);
        
        return buffer;
    }

    /// <summary>
    /// Parsed unison phrase hit data.
    /// </summary>
    public readonly struct ParsedPhraseHit
    {
        public string PlayerName { get; init; }
        public double PhraseStartTime { get; init; }
        public double PhraseEndTime { get; init; }
    }

    /// <summary>
    /// Parses a unison phrase hit packet.
    /// </summary>
    public static bool TryParsePhraseHitPacket(ReadOnlySpan<byte> data, out ParsedPhraseHit result)
    {
        result = default;
        
        if (data.Length < 19) // Type + min name + 2 doubles
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            result = new ParsedPhraseHit
            {
                PlayerName = reader.ReadString(),
                PhraseStartTime = reader.ReadDouble(),
                PhraseEndTime = reader.ReadDouble()
            };
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a unison bonus award packet.
    /// </summary>
    public static bool TryParseBonusAwardPacket(ReadOnlySpan<byte> data, out double phraseStartTime)
    {
        phraseStartTime = 0;
        
        if (data.Length < 9) // Type + double
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            phraseStartTime = reader.ReadDouble();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

#endregion
