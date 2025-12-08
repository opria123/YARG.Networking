using System;
using System.Collections.Generic;

namespace YARG.Net.Packets;

public sealed record GameplayCountdownPacket(Guid SessionId, int SecondsRemaining) : IPacketPayload;

public sealed record GameplayInputFramePacket(Guid SessionId, long FrameNumber, IReadOnlyList<InputEvent> Inputs) : IPacketPayload;

public sealed record InputEvent(Guid PlayerId, string Input, double Value, long Timestamp);

/// <summary>
/// Sent by clients to broadcast their gameplay state to other players.
/// Replicated via server to all connected clients.
/// </summary>
public sealed record GameplayStatePacket(
    Guid SessionId,
    uint Sequence,
    int Score,
    int Combo,
    int Streak,
    bool StarPowerActive,
    float StarPowerAmount,
    int StarPowerPhrasesHit,
    int TotalStarPowerPhrases,
    int NotesHit,
    int NotesMissed,
    int Overstrums,
    int HoposStrummed,
    int Overhits,
    int GhostInputs,
    int GhostsHit,
    int AccentsHit,
    int DynamicsBonus,
    int BandBonusScore,
    int VocalsTicksHit,
    int VocalsTicksMissed,
    float VocalsPhraseTicksHit,
    int VocalsPhraseTicksTotal,
    bool SoloActive,
    int SoloSequence,
    int SoloNoteCount,
    int SoloNotesHit,
    int SoloLastBonus,
    int SoloTotalBonus,
    double SongTime,
    double ClientNetworkTime) : IPacketPayload;

/// <summary>
/// Sent by the host to signal gameplay is starting with synchronized timing.
/// </summary>
public sealed record GameplayStartPacket(
    Guid LobbyId,
    double ServerTime,
    double SongStartTime) : IPacketPayload;

/// <summary>
/// Sent to sync chart time across clients during gameplay.
/// </summary>
public sealed record GameplayTimeSyncPacket(
    Guid LobbyId,
    double ServerTime,
    double SongTime) : IPacketPayload;

/// <summary>
/// Sent when a player pauses or resumes gameplay.
/// </summary>
public sealed record GameplayPausePacket(
    Guid SessionId,
    bool IsPaused,
    double PauseTime) : IPacketPayload;

/// <summary>
/// Sent when gameplay ends (song complete, fail, or quit).
/// </summary>
public sealed record GameplayEndPacket(
    Guid LobbyId,
    GameplayEndReason Reason) : IPacketPayload;

public enum GameplayEndReason
{
    SongComplete,
    AllPlayersFailed,
    HostEnded,
    Disconnected,
}

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for gameplay-related messages.
/// </summary>
public static class GameplayBinaryPackets
{
    /// <summary>
    /// Builds a gameplay start packet.
    /// </summary>
    public static byte[] BuildStartPacket()
    {
        return new byte[] { (byte)PacketType.GameplayStart };
    }

    /// <summary>
    /// Builds a gameplay restart packet.
    /// </summary>
    public static byte[] BuildRestartPacket()
    {
        return new byte[] { (byte)PacketType.GameplayRestart };
    }

    /// <summary>
    /// Builds a quit to library packet.
    /// </summary>
    public static byte[] BuildQuitToLibraryPacket()
    {
        return new byte[] { (byte)PacketType.QuitToLibrary };
    }

    /// <summary>
    /// Builds a player left gameplay packet.
    /// </summary>
    public static byte[] BuildPlayerLeftPacket(string playerName)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(playerName ?? string.Empty);
        byte[] buffer = new byte[1 + 2 + nameBytes.Length];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.PlayerLeftGameplay);
        writer.WriteString(playerName);
        
        return buffer;
    }

    /// <summary>
    /// Parses a player left gameplay packet.
    /// </summary>
    public static bool TryParsePlayerLeftPacket(ReadOnlySpan<byte> data, out string playerName)
    {
        playerName = string.Empty;
        
        if (data.Length < 3)
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            playerName = reader.ReadString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a gameplay snapshot packet with all player stats.
    /// </summary>
    public static byte[] BuildSnapshotPacket(
        string playerName,
        int sequenceNumber,
        int score,
        int combo,
        int streak,
        bool starPowerActive,
        float starPowerAmount,
        int starPowerPhrasesHit,
        int totalStarPowerPhrases,
        int notesHit,
        int notesMissed,
        int overstrums,
        int hoposStrummed,
        int overhits,
        int ghostInputs,
        int ghostsHit,
        int accentsHit,
        int dynamicsBonus,
        int bandBonusScore,
        int vocalsTicksHit,
        int vocalsTicksMissed,
        float vocalsPhraseTicksHit,
        int vocalsPhraseTicksTotal,
        double songPosition,
        bool isPaused,
        bool hasFailed,
        bool isInPractice)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(playerName ?? string.Empty);
        // Format: [Type(1)][NameLen(2)][Name][Seq(4)][Score(4)][Combo(4)][Streak(4)]
        //         [SPActive(1)][SPAmount(4)][SPPhrasesHit(4)][TotalSPPhrases(4)]
        //         [NotesHit(4)][NotesMissed(4)][Overstrums(4)][HoposStrummed(4)][Overhits(4)][GhostInputs(4)]
        //         [GhostsHit(4)][AccentsHit(4)][DynamicsBonus(4)][BandBonus(4)]
        //         [VocalsHit(4)][VocalsMissed(4)][VocalsPhraseHit(4)][VocalsPhrasesTotal(4)]
        //         [SongPos(8)][IsPaused(1)][HasFailed(1)][IsInPractice(1)]
        // Total fixed: 4+4+4+4 + 1+4+4+4 + 4+4+4+4+4+4 + 4+4+4+4 + 4+4+4+4 + 8+1+1+1 = 99 bytes
        byte[] buffer = new byte[1 + 2 + nameBytes.Length + 99];
        var writer = new PacketWriter(buffer);
        
        writer.WritePacketType(PacketType.GameplayState);
        writer.WriteString(playerName);
        writer.WriteInt32(sequenceNumber);
        writer.WriteInt32(score);
        writer.WriteInt32(combo);
        writer.WriteInt32(streak);
        writer.WriteBool(starPowerActive);
        writer.WriteFloat(starPowerAmount);
        writer.WriteInt32(starPowerPhrasesHit);
        writer.WriteInt32(totalStarPowerPhrases);
        writer.WriteInt32(notesHit);
        writer.WriteInt32(notesMissed);
        writer.WriteInt32(overstrums);
        writer.WriteInt32(hoposStrummed);
        writer.WriteInt32(overhits);
        writer.WriteInt32(ghostInputs);
        writer.WriteInt32(ghostsHit);
        writer.WriteInt32(accentsHit);
        writer.WriteInt32(dynamicsBonus);
        writer.WriteInt32(bandBonusScore);
        writer.WriteInt32(vocalsTicksHit);
        writer.WriteInt32(vocalsTicksMissed);
        writer.WriteFloat(vocalsPhraseTicksHit);
        writer.WriteInt32(vocalsPhraseTicksTotal);
        writer.WriteDouble(songPosition);
        writer.WriteBool(isPaused);
        writer.WriteBool(hasFailed);
        writer.WriteBool(isInPractice);
        
        return buffer;
    }

    /// <summary>
    /// Data structure for parsed gameplay snapshot.
    /// </summary>
    public readonly struct SnapshotData
    {
        public string PlayerName { get; init; }
        public int SequenceNumber { get; init; }
        public int Score { get; init; }
        public int Combo { get; init; }
        public int Streak { get; init; }
        public bool StarPowerActive { get; init; }
        public float StarPowerAmount { get; init; }
        public int StarPowerPhrasesHit { get; init; }
        public int TotalStarPowerPhrases { get; init; }
        public int NotesHit { get; init; }
        public int NotesMissed { get; init; }
        public int Overstrums { get; init; }
        public int HoposStrummed { get; init; }
        public int Overhits { get; init; }
        public int GhostInputs { get; init; }
        public int GhostsHit { get; init; }
        public int AccentsHit { get; init; }
        public int DynamicsBonus { get; init; }
        public int BandBonusScore { get; init; }
        public int VocalsTicksHit { get; init; }
        public int VocalsTicksMissed { get; init; }
        public float VocalsPhraseTicksHit { get; init; }
        public int VocalsPhraseTicksTotal { get; init; }
        public double SongPosition { get; init; }
        public bool IsPaused { get; init; }
        public bool HasFailed { get; init; }
        public bool IsInPractice { get; init; }
    }

    /// <summary>
    /// Parses a gameplay snapshot packet.
    /// </summary>
    public static bool TryParseSnapshotPacket(ReadOnlySpan<byte> data, out SnapshotData snapshot)
    {
        snapshot = default;
        
        if (data.Length < 3 + 99) // Type + min name len + fixed fields
            return false;

        var reader = new PacketReader(data);
        reader.Skip(1); // Skip packet type
        
        try
        {
            snapshot = new SnapshotData
            {
                PlayerName = reader.ReadString(),
                SequenceNumber = reader.ReadInt32(),
                Score = reader.ReadInt32(),
                Combo = reader.ReadInt32(),
                Streak = reader.ReadInt32(),
                StarPowerActive = reader.ReadBool(),
                StarPowerAmount = reader.ReadFloat(),
                StarPowerPhrasesHit = reader.ReadInt32(),
                TotalStarPowerPhrases = reader.ReadInt32(),
                NotesHit = reader.ReadInt32(),
                NotesMissed = reader.ReadInt32(),
                Overstrums = reader.ReadInt32(),
                HoposStrummed = reader.ReadInt32(),
                Overhits = reader.ReadInt32(),
                GhostInputs = reader.ReadInt32(),
                GhostsHit = reader.ReadInt32(),
                AccentsHit = reader.ReadInt32(),
                DynamicsBonus = reader.ReadInt32(),
                BandBonusScore = reader.ReadInt32(),
                VocalsTicksHit = reader.ReadInt32(),
                VocalsTicksMissed = reader.ReadInt32(),
                VocalsPhraseTicksHit = reader.ReadFloat(),
                VocalsPhraseTicksTotal = reader.ReadInt32(),
                SongPosition = reader.ReadDouble(),
                IsPaused = reader.ReadBool(),
                HasFailed = reader.ReadBool(),
                IsInPractice = reader.ReadBool()
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
