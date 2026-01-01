using System;

namespace YARG.Net.Packets;

/// <summary>
/// Represents a gameplay state snapshot for network synchronization.
/// </summary>
public readonly struct GameplaySnapshot
{
    // Core state
    public uint Sequence { get; init; }
    public int Score { get; init; }
    public int Combo { get; init; }
    public int Streak { get; init; }
    
    // Star power
    public bool StarPowerActive { get; init; }
    public float StarPowerAmount { get; init; }
    public int StarPowerPhrasesHit { get; init; }
    public int TotalStarPowerPhrases { get; init; }
    
    // Notes
    public int NotesHit { get; init; }
    public int NotesMissed { get; init; }
    
    // Guitar-specific
    public int Overstrums { get; init; }
    public int HoposStrummed { get; init; }
    public int Overhits { get; init; }
    public int GhostInputs { get; init; }
    
    // Drums-specific
    public int GhostsHit { get; init; }
    public int AccentsHit { get; init; }
    public int DynamicsBonus { get; init; }
    
    // Band bonus
    public int BandBonusScore { get; init; }
    
    // Vocals
    public int VocalsTicksHit { get; init; }
    public int VocalsTicksMissed { get; init; }
    public float VocalsPhraseTicksHit { get; init; }
    public int VocalsPhraseTicksTotal { get; init; }
    
    // Solo
    public bool SoloActive { get; init; }
    public int SoloSequence { get; init; }
    public int SoloNoteCount { get; init; }
    public int SoloNotesHit { get; init; }
    public int SoloLastBonus { get; init; }
    public int SoloTotalBonus { get; init; }
    
    // Sustain/Whammy
    public int SustainsHeld { get; init; }
    public float WhammyValue { get; init; }
    
    // Stars progress
    public float Stars { get; init; }
    
    // Timing
    public double SongTime { get; init; }
    
    // Player identification
    public string PlayerName { get; init; }
    
    // Fail state (synced from authoritative client)
    /// <summary>
    /// Player's current happiness/rock meter value (0.0 to 1.0).
    /// Synced from the player's local client which is the source of truth.
    /// </summary>
    public float Happiness { get; init; }
    
    /// <summary>
    /// Whether the player has failed (happiness dropped to 0).
    /// Synced from the player's local client which is the source of truth.
    /// </summary>
    public bool HasFailed { get; init; }
}

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for gameplay state synchronization.
/// </summary>
public static class GameplayStateBinaryPackets
{
    /// <summary>
    /// Minimum packet size without player name.
    /// 1 (type) + 4 (seq) + 4*3 (score/combo/streak) + 1+4+4+4 (SP) + 4*2 (notes) + 
    /// 4*4 (guitar) + 4*3 (drums) + 4 (band) + 4*2+4+4 (vocals) + 1+4*6 (solo) + 4+4 (sustain/whammy) + 4 (stars) + 8 (time) + 2 (nameLen) + 4+1 (happiness/failed)
    /// = 1 + 4 + 12 + 13 + 8 + 16 + 12 + 4 + 16 + 25 + 8 + 4 + 8 + 2 + 5 = 138
    /// </summary>
    public const int MinPacketSize = 138;
    
    /// <summary>
    /// Builds a gameplay state snapshot packet.
    /// </summary>
    public static byte[] BuildSnapshotPacket(in GameplaySnapshot snapshot)
    {
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(snapshot.PlayerName ?? string.Empty);
        byte[] message = new byte[MinPacketSize + nameBytes.Length];
        int offset = 0;
        
        message[offset++] = (byte)PacketType.GameplayState;
        
        // Sequence
        WriteInt32(message, ref offset, (int)snapshot.Sequence);
        
        // Score, combo, streak
        WriteInt32(message, ref offset, snapshot.Score);
        WriteInt32(message, ref offset, snapshot.Combo);
        WriteInt32(message, ref offset, snapshot.Streak);
        
        // Star power
        message[offset++] = snapshot.StarPowerActive ? (byte)1 : (byte)0;
        WriteFloat(message, ref offset, snapshot.StarPowerAmount);
        WriteInt32(message, ref offset, snapshot.StarPowerPhrasesHit);
        WriteInt32(message, ref offset, snapshot.TotalStarPowerPhrases);
        
        // Notes
        WriteInt32(message, ref offset, snapshot.NotesHit);
        WriteInt32(message, ref offset, snapshot.NotesMissed);
        
        // Guitar stats
        WriteInt32(message, ref offset, snapshot.Overstrums);
        WriteInt32(message, ref offset, snapshot.HoposStrummed);
        WriteInt32(message, ref offset, snapshot.Overhits);
        WriteInt32(message, ref offset, snapshot.GhostInputs);
        
        // Drums stats
        WriteInt32(message, ref offset, snapshot.GhostsHit);
        WriteInt32(message, ref offset, snapshot.AccentsHit);
        WriteInt32(message, ref offset, snapshot.DynamicsBonus);
        
        // Band bonus
        WriteInt32(message, ref offset, snapshot.BandBonusScore);
        
        // Vocals
        WriteInt32(message, ref offset, snapshot.VocalsTicksHit);
        WriteInt32(message, ref offset, snapshot.VocalsTicksMissed);
        WriteFloat(message, ref offset, snapshot.VocalsPhraseTicksHit);
        WriteInt32(message, ref offset, snapshot.VocalsPhraseTicksTotal);
        
        // Solo
        message[offset++] = snapshot.SoloActive ? (byte)1 : (byte)0;
        WriteInt32(message, ref offset, snapshot.SoloSequence);
        WriteInt32(message, ref offset, snapshot.SoloNoteCount);
        WriteInt32(message, ref offset, snapshot.SoloNotesHit);
        WriteInt32(message, ref offset, snapshot.SoloLastBonus);
        WriteInt32(message, ref offset, snapshot.SoloTotalBonus);
        
        // Sustain and whammy
        WriteInt32(message, ref offset, snapshot.SustainsHeld);
        WriteFloat(message, ref offset, snapshot.WhammyValue);
        
        // Stars progress
        WriteFloat(message, ref offset, snapshot.Stars);
        
        // Song time
        WriteDouble(message, ref offset, snapshot.SongTime);
        
        // Happiness and fail state (authoritative from local client)
        WriteFloat(message, ref offset, snapshot.Happiness);
        message[offset++] = snapshot.HasFailed ? (byte)1 : (byte)0;
        
        // Player name
        message[offset++] = (byte)(nameBytes.Length >> 8);
        message[offset++] = (byte)(nameBytes.Length & 0xFF);
        Array.Copy(nameBytes, 0, message, offset, nameBytes.Length);
        
        return message;
    }
    
    /// <summary>
    /// Parsed gameplay snapshot data.
    /// </summary>
    public readonly struct ParsedSnapshot
    {
        public bool IsValid { get; init; }
        public GameplaySnapshot Snapshot { get; init; }
    }
    
    /// <summary>
    /// Parses a gameplay state snapshot packet.
    /// </summary>
    public static ParsedSnapshot ParseSnapshotPacket(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinPacketSize)
            return new ParsedSnapshot { IsValid = false };
        
        int offset = 1; // Skip packet type
        
        // Parse all fields
        uint sequence = (uint)ReadInt32(data, ref offset);
        int score = ReadInt32(data, ref offset);
        int combo = ReadInt32(data, ref offset);
        int streak = ReadInt32(data, ref offset);
        
        bool starPowerActive = data[offset++] == 1;
        float starPowerAmount = ReadFloat(data, ref offset);
        int starPowerPhrasesHit = ReadInt32(data, ref offset);
        int totalStarPowerPhrases = ReadInt32(data, ref offset);
        
        int notesHit = ReadInt32(data, ref offset);
        int notesMissed = ReadInt32(data, ref offset);
        
        int overstrums = ReadInt32(data, ref offset);
        int hoposStrummed = ReadInt32(data, ref offset);
        int overhits = ReadInt32(data, ref offset);
        int ghostInputs = ReadInt32(data, ref offset);
        
        int ghostsHit = ReadInt32(data, ref offset);
        int accentsHit = ReadInt32(data, ref offset);
        int dynamicsBonus = ReadInt32(data, ref offset);
        
        int bandBonusScore = ReadInt32(data, ref offset);
        
        int vocalsTicksHit = ReadInt32(data, ref offset);
        int vocalsTicksMissed = ReadInt32(data, ref offset);
        float vocalsPhraseTicksHit = ReadFloat(data, ref offset);
        int vocalsPhraseTicksTotal = ReadInt32(data, ref offset);
        
        bool soloActive = data[offset++] == 1;
        int soloSequence = ReadInt32(data, ref offset);
        int soloNoteCount = ReadInt32(data, ref offset);
        int soloNotesHit = ReadInt32(data, ref offset);
        int soloLastBonus = ReadInt32(data, ref offset);
        int soloTotalBonus = ReadInt32(data, ref offset);
        
        int sustainsHeld = ReadInt32(data, ref offset);
        float whammyValue = ReadFloat(data, ref offset);
        
        float stars = ReadFloat(data, ref offset);
        
        double songTime = ReadDouble(data, ref offset);
        
        // Happiness and fail state
        float happiness = ReadFloat(data, ref offset);
        bool hasFailed = data[offset++] == 1;
        
        // Player name
        if (data.Length < offset + 2)
            return new ParsedSnapshot { IsValid = false };
        
        int nameLen = (data[offset] << 8) | data[offset + 1];
        offset += 2;
        
        if (data.Length < offset + nameLen)
            return new ParsedSnapshot { IsValid = false };
        
        string playerName = System.Text.Encoding.UTF8.GetString(data.Slice(offset, nameLen));
        
        return new ParsedSnapshot
        {
            IsValid = true,
            Snapshot = new GameplaySnapshot
            {
                Sequence = sequence,
                Score = score,
                Combo = combo,
                Streak = streak,
                StarPowerActive = starPowerActive,
                StarPowerAmount = starPowerAmount,
                StarPowerPhrasesHit = starPowerPhrasesHit,
                TotalStarPowerPhrases = totalStarPowerPhrases,
                NotesHit = notesHit,
                NotesMissed = notesMissed,
                Overstrums = overstrums,
                HoposStrummed = hoposStrummed,
                Overhits = overhits,
                GhostInputs = ghostInputs,
                GhostsHit = ghostsHit,
                AccentsHit = accentsHit,
                DynamicsBonus = dynamicsBonus,
                BandBonusScore = bandBonusScore,
                VocalsTicksHit = vocalsTicksHit,
                VocalsTicksMissed = vocalsTicksMissed,
                VocalsPhraseTicksHit = vocalsPhraseTicksHit,
                VocalsPhraseTicksTotal = vocalsPhraseTicksTotal,
                SoloActive = soloActive,
                SoloSequence = soloSequence,
                SoloNoteCount = soloNoteCount,
                SoloNotesHit = soloNotesHit,
                SoloLastBonus = soloLastBonus,
                SoloTotalBonus = soloTotalBonus,
                SustainsHeld = sustainsHeld,
                WhammyValue = whammyValue,
                Stars = stars,
                SongTime = songTime,
                Happiness = happiness,
                HasFailed = hasFailed,
                PlayerName = playerName
            }
        };
    }
    
    #region Binary Helpers
    
    private static void WriteInt32(byte[] buffer, ref int offset, int value)
    {
        buffer[offset++] = (byte)(value >> 24);
        buffer[offset++] = (byte)(value >> 16);
        buffer[offset++] = (byte)(value >> 8);
        buffer[offset++] = (byte)value;
    }
    
    private static void WriteFloat(byte[] buffer, ref int offset, float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            buffer[offset++] = bytes[3];
            buffer[offset++] = bytes[2];
            buffer[offset++] = bytes[1];
            buffer[offset++] = bytes[0];
        }
        else
        {
            Array.Copy(bytes, 0, buffer, offset, 4);
            offset += 4;
        }
    }
    
    private static void WriteDouble(byte[] buffer, ref int offset, double value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            for (int i = 7; i >= 0; i--)
                buffer[offset++] = bytes[i];
        }
        else
        {
            Array.Copy(bytes, 0, buffer, offset, 8);
            offset += 8;
        }
    }
    
    private static int ReadInt32(ReadOnlySpan<byte> span, ref int offset)
    {
        int value = (span[offset] << 24) | (span[offset + 1] << 16) | (span[offset + 2] << 8) | span[offset + 3];
        offset += 4;
        return value;
    }
    
    private static float ReadFloat(ReadOnlySpan<byte> span, ref int offset)
    {
        byte[] bytes = new byte[4];
        if (BitConverter.IsLittleEndian)
        {
            bytes[3] = span[offset++];
            bytes[2] = span[offset++];
            bytes[1] = span[offset++];
            bytes[0] = span[offset++];
        }
        else
        {
            span.Slice(offset, 4).CopyTo(bytes);
            offset += 4;
        }
        return BitConverter.ToSingle(bytes, 0);
    }
    
    private static double ReadDouble(ReadOnlySpan<byte> span, ref int offset)
    {
        byte[] bytes = new byte[8];
        if (BitConverter.IsLittleEndian)
        {
            for (int i = 7; i >= 0; i--)
                bytes[i] = span[offset++];
        }
        else
        {
            span.Slice(offset, 8).CopyTo(bytes);
            offset += 8;
        }
        return BitConverter.ToDouble(bytes, 0);
    }
    
    #endregion
}

#endregion
