using System;

namespace YARG.Net.Packets;

/// <summary>
/// Sent by a player when they successfully complete a unison star power phrase.
/// </summary>
public sealed record UnisonPhraseHitPacket(
    Guid SessionId,
    string PlayerName,
    int BandId,
    double PhraseTime,
    double PhraseEndTime) : IPacketPayload;

/// <summary>
/// Sent by the host when all players in a band have completed a unison phrase
/// and the bonus should be awarded.
/// </summary>
public sealed record UnisonBonusAwardPacket(
    Guid LobbyId,
    int BandId,
    double PhraseTime) : IPacketPayload;

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for unison-related messages.
/// Supports per-band unison tracking.
/// </summary>
public static class UnisonBinaryPackets
{
    /// <summary>
    /// Builds a unison phrase hit packet with band ID.
    /// </summary>
    public static byte[] BuildPhraseHitPacket(string playerName, int bandId, double phraseStartTime, double phraseEndTime)
    {
        int size = 1 + PacketWriter.GetStringSize(playerName) + 4 + 16; // Type + name + int + 2 doubles
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.UnisonPhraseHit);
        writer.WriteString(playerName);
        writer.WriteInt32(bandId);
        writer.WriteDouble(phraseStartTime);
        writer.WriteDouble(phraseEndTime);
        
        return buffer;
    }
    
    /// <summary>
    /// Builds a unison phrase hit packet (legacy, bandId=0).
    /// </summary>
    public static byte[] BuildPhraseHitPacket(string playerName, double phraseStartTime, double phraseEndTime)
    {
        return BuildPhraseHitPacket(playerName, 0, phraseStartTime, phraseEndTime);
    }

    /// <summary>
    /// Builds a unison bonus award packet with band ID.
    /// </summary>
    public static byte[] BuildBonusAwardPacket(int bandId, double phraseStartTime)
    {
        byte[] buffer = new byte[13]; // Type + int + double
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.UnisonBonusAward);
        writer.WriteInt32(bandId);
        writer.WriteDouble(phraseStartTime);
        
        return buffer;
    }
    
    /// <summary>
    /// Builds a unison bonus award packet (legacy, bandId=0).
    /// </summary>
    public static byte[] BuildBonusAwardPacket(double phraseStartTime)
    {
        return BuildBonusAwardPacket(0, phraseStartTime);
    }

    /// <summary>
    /// Parsed unison phrase hit data.
    /// </summary>
    public readonly struct ParsedPhraseHit
    {
        public string PlayerName { get; init; }
        public int BandId { get; init; }
        public double PhraseStartTime { get; init; }
        public double PhraseEndTime { get; init; }
    }

    /// <summary>
    /// Parses a unison phrase hit packet.
    /// </summary>
    public static bool TryParsePhraseHitPacket(ReadOnlySpan<byte> data, out ParsedPhraseHit result)
    {
        result = default;
        
        if (data.Length < 23) // Type + min name + int + 2 doubles
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            result = new ParsedPhraseHit
            {
                PlayerName = reader.ReadString(),
                BandId = reader.ReadInt32(),
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
    /// Parses a unison bonus award packet with band ID.
    /// </summary>
    public static bool TryParseBonusAwardPacket(ReadOnlySpan<byte> data, out int bandId, out double phraseStartTime)
    {
        bandId = 0;
        phraseStartTime = 0;
        
        if (data.Length < 13) // Type + int + double
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            bandId = reader.ReadInt32();
            phraseStartTime = reader.ReadDouble();
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Parses a unison bonus award packet (legacy overload, ignores bandId).
    /// </summary>
    public static bool TryParseBonusAwardPacket(ReadOnlySpan<byte> data, out double phraseStartTime)
    {
        return TryParseBonusAwardPacket(data, out _, out phraseStartTime);
    }
}

#endregion
