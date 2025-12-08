using System;

namespace YARG.Net.Packets;

/// <summary>
/// Sent by the host to advance past the score screen.
/// </summary>
public sealed record ScoreScreenAdvancePacket(
    Guid LobbyId,
    bool HasMoreSongs) : IPacketPayload;

/// <summary>
/// Sent by players to share their score results after completing a song.
/// </summary>
public sealed record ScoreResultsPacket(
    Guid SessionId,
    string PlayerName,
    bool IsHighScore,
    bool IsFullCombo,
    int Score,
    int MaxCombo,
    int NotesHit,
    int NotesMissed) : IPacketPayload;

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for score-related messages.
/// </summary>
public static class ScoreBinaryPackets
{
    /// <summary>
    /// Builds a score screen advance packet.
    /// </summary>
    public static byte[] BuildAdvancePacket(int advanceIndex)
    {
        return new byte[] { (byte)PacketType.ScoreScreenAdvance, (byte)advanceIndex };
    }

    /// <summary>
    /// Builds a score results packet.
    /// </summary>
    public static byte[] BuildResultsPacket(
        string playerName,
        int finalScore,
        int notesHit,
        int notesMissed,
        int maxCombo,
        int starCount,
        bool fullCombo)
    {
        int size = 1 + PacketWriter.GetStringSize(playerName) + 21; // Type + name + 5 ints + bool
        byte[] buffer = new byte[size];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.ScoreResults);
        writer.WriteString(playerName);
        writer.WriteInt32(finalScore);
        writer.WriteInt32(notesHit);
        writer.WriteInt32(notesMissed);
        writer.WriteInt32(maxCombo);
        writer.WriteInt32(starCount);
        writer.WriteBool(fullCombo);
        
        return buffer;
    }

    /// <summary>
    /// Parses a score screen advance packet.
    /// </summary>
    public static bool TryParseAdvancePacket(ReadOnlySpan<byte> data, out int advanceIndex)
    {
        advanceIndex = 0;
        
        if (data.Length < 2)
            return false;

        advanceIndex = data[1];
        return true;
    }

    /// <summary>
    /// Parsed score results data.
    /// </summary>
    public readonly struct ParsedScoreResults
    {
        public string PlayerName { get; init; }
        public int FinalScore { get; init; }
        public int NotesHit { get; init; }
        public int NotesMissed { get; init; }
        public int MaxCombo { get; init; }
        public int StarCount { get; init; }
        public bool FullCombo { get; init; }
    }

    /// <summary>
    /// Parses a score results packet.
    /// </summary>
    public static bool TryParseResultsPacket(ReadOnlySpan<byte> data, out ParsedScoreResults result)
    {
        result = default;
        
        if (data.Length < 24) // Type + min name + 5 ints + bool
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            result = new ParsedScoreResults
            {
                PlayerName = reader.ReadString(),
                FinalScore = reader.ReadInt32(),
                NotesHit = reader.ReadInt32(),
                NotesMissed = reader.ReadInt32(),
                MaxCombo = reader.ReadInt32(),
                StarCount = reader.ReadInt32(),
                FullCombo = reader.ReadBool()
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
