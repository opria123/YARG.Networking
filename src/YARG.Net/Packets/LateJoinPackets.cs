using System;
using System.Collections.Generic;
using System.Text;

namespace YARG.Net.Packets;

/// <summary>
/// Represents the current phase of a multiplayer session.
/// Used for late join handling to determine what state new joiners should enter.
/// </summary>
public enum SessionPhase : byte
{
    /// <summary>
    /// In the lobby/music library - not a late join.
    /// </summary>
    Lobby = 0,
    
    /// <summary>
    /// In music library selecting songs - not a late join.
    /// </summary>
    MusicLibrary = 1,
    
    /// <summary>
    /// In difficulty selection for a setlist song.
    /// Late joiners need to have the current/remaining setlist songs.
    /// </summary>
    DifficultySelect = 2,
    
    /// <summary>
    /// Countdown before song starts.
    /// Late joiners need to have the current song.
    /// </summary>
    Countdown = 3,
    
    /// <summary>
    /// Song is currently being played.
    /// Late joiners with the song can spectate, without must wait.
    /// </summary>
    PlayingSong = 4,
    
    /// <summary>
    /// On the score screen after a song.
    /// Treated similar to DifficultySelect for late join purposes.
    /// </summary>
    ScoreScreen = 5,
}

/// <summary>
/// Late joiner status - what action they should take.
/// </summary>
public enum LateJoinAction : byte
{
    /// <summary>
    /// Not a late join - normal join flow.
    /// </summary>
    NormalJoin = 0,
    
    /// <summary>
    /// Late joiner can spectate the current song, will join next song.
    /// </summary>
    SpectateCurrentSong = 1,
    
    /// <summary>
    /// Late joiner doesn't have the current song - must wait.
    /// </summary>
    WaitForSongEnd = 2,
    
    /// <summary>
    /// Late joiner doesn't have remaining setlist songs - abort setlist for everyone.
    /// </summary>
    AbortSetlist = 3,
    
    /// <summary>
    /// Late join during diff select with all songs - immediate join.
    /// </summary>
    JoinDifficultySelect = 4,
    
    /// <summary>
    /// Late join rejected - host has disabled late joining.
    /// Client should show message and disconnect cleanly.
    /// </summary>
    Rejected = 5,
}

/// <summary>
/// Sent by host to a late-joining client to describe the current session state.
/// </summary>
public sealed record LateJoinStatePacket(
    Guid LobbyId,
    SessionPhase Phase,
    string CurrentSongHash,
    IReadOnlyList<string> RemainingSetlistHashes) : IPacketPayload;

/// <summary>
/// Sent by client to host in response to LateJoinState, indicating which songs they have.
/// </summary>
public sealed record LateJoinSongCheckResponsePacket(
    Guid PlayerId,
    bool HasCurrentSong,
    IReadOnlyList<string> OwnedSetlistHashes) : IPacketPayload;

/// <summary>
/// Sent by host to late-joining client (and optionally all clients) with the late join decision.
/// </summary>
public sealed record LateJoinActionPacket(
    Guid LobbyId,
    Guid PlayerId,
    string PlayerName,
    LateJoinAction Action,
    string Message,
    double SongTime = 0) : IPacketPayload;

/// <summary>
/// Sent by host to all clients when the setlist must be aborted due to a late joiner.
/// </summary>
public sealed record SetlistAbortPacket(
    Guid LobbyId,
    string Reason) : IPacketPayload;

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for late join messages.
/// </summary>
public static class LateJoinBinaryPackets
{
    /// <summary>
    /// Builds a late join state packet.
    /// Format: [PacketType (1)][Phase (1)][CurrentSongHashLen (2)][CurrentSongHash][SetlistCount (2)][SetlistHashes...]
    /// </summary>
    public static byte[] BuildLateJoinStatePacket(SessionPhase phase, string currentSongHash, IReadOnlyList<string> remainingSetlistHashes)
    {
        currentSongHash ??= string.Empty;
        remainingSetlistHashes ??= Array.Empty<string>();
        
        byte[] currentSongBytes = Encoding.UTF8.GetBytes(currentSongHash);
        
        // Calculate total size
        int size = 1 // PacketType
                 + 1 // Phase
                 + 2 + currentSongBytes.Length // Current song hash
                 + 2; // Setlist count
        
        // Add setlist hashes sizes
        var setlistBytesList = new List<byte[]>();
        foreach (var hash in remainingSetlistHashes)
        {
            var hashBytes = Encoding.UTF8.GetBytes(hash ?? string.Empty);
            setlistBytesList.Add(hashBytes);
            size += 2 + hashBytes.Length;
        }
        
        byte[] buffer = new byte[size];
        int offset = 0;
        
        // Write packet type
        buffer[offset++] = (byte)PacketType.LateJoinState;
        
        // Write phase
        buffer[offset++] = (byte)phase;
        
        // Write current song hash
        WriteShortString(buffer, ref offset, currentSongBytes);
        
        // Write setlist count and hashes
        buffer[offset++] = (byte)(remainingSetlistHashes.Count >> 8);
        buffer[offset++] = (byte)(remainingSetlistHashes.Count & 0xFF);
        
        foreach (var hashBytes in setlistBytesList)
        {
            WriteShortString(buffer, ref offset, hashBytes);
        }
        
        return buffer;
    }
    
    /// <summary>
    /// Parses a late join state packet.
    /// </summary>
    public static bool TryParseLateJoinStatePacket(ReadOnlySpan<byte> data, out SessionPhase phase, out string currentSongHash, out List<string> remainingSetlistHashes)
    {
        phase = SessionPhase.Lobby;
        currentSongHash = string.Empty;
        remainingSetlistHashes = new List<string>();
        
        // Minimum size: type (1) + phase (1) + currentHashLen (2) + setlistCount (2) = 6
        if (data.Length < 6)
            return false;
        
        int offset = 1; // Skip packet type
        
        phase = (SessionPhase)data[offset++];
        
        if (!TryReadShortString(data, ref offset, out currentSongHash))
            return false;
        
        if (offset + 2 > data.Length)
            return false;
        
        int setlistCount = (data[offset++] << 8) | data[offset++];
        
        for (int i = 0; i < setlistCount; i++)
        {
            if (!TryReadShortString(data, ref offset, out string hash))
                return false;
            remainingSetlistHashes.Add(hash);
        }
        
        return true;
    }
    
    /// <summary>
    /// Builds a late join song check response packet.
    /// Format: [PacketType (1)][PlayerId (16)][HasCurrentSong (1)][OwnedCount (2)][OwnedHashes...]
    /// </summary>
    public static byte[] BuildSongCheckResponsePacket(Guid playerId, bool hasCurrentSong, IReadOnlyList<string> ownedSetlistHashes)
    {
        ownedSetlistHashes ??= Array.Empty<string>();
        
        // Calculate total size
        int size = 1 // PacketType
                 + 16 // PlayerId
                 + 1 // HasCurrentSong
                 + 2; // Owned count
        
        var ownedBytesList = new List<byte[]>();
        foreach (var hash in ownedSetlistHashes)
        {
            var hashBytes = Encoding.UTF8.GetBytes(hash ?? string.Empty);
            ownedBytesList.Add(hashBytes);
            size += 2 + hashBytes.Length;
        }
        
        byte[] buffer = new byte[size];
        int offset = 0;
        
        buffer[offset++] = (byte)PacketType.LateJoinSongCheckResponse;
        
        // Write player ID
        var playerIdBytes = playerId.ToByteArray();
        Array.Copy(playerIdBytes, 0, buffer, offset, 16);
        offset += 16;
        
        buffer[offset++] = hasCurrentSong ? (byte)1 : (byte)0;
        
        buffer[offset++] = (byte)(ownedSetlistHashes.Count >> 8);
        buffer[offset++] = (byte)(ownedSetlistHashes.Count & 0xFF);
        
        foreach (var hashBytes in ownedBytesList)
        {
            WriteShortString(buffer, ref offset, hashBytes);
        }
        
        return buffer;
    }
    
    /// <summary>
    /// Parses a late join song check response packet.
    /// </summary>
    public static bool TryParseSongCheckResponsePacket(ReadOnlySpan<byte> data, out Guid playerId, out bool hasCurrentSong, out List<string> ownedSetlistHashes)
    {
        playerId = Guid.Empty;
        hasCurrentSong = false;
        ownedSetlistHashes = new List<string>();
        
        // Minimum size: type (1) + playerId (16) + hasCurrentSong (1) + ownedCount (2) = 20
        if (data.Length < 20)
            return false;
        
        int offset = 1;
        
        playerId = new Guid(data.Slice(offset, 16).ToArray());
        offset += 16;
        
        hasCurrentSong = data[offset++] != 0;
        
        int ownedCount = (data[offset++] << 8) | data[offset++];
        
        for (int i = 0; i < ownedCount; i++)
        {
            if (!TryReadShortString(data, ref offset, out string hash))
                return false;
            ownedSetlistHashes.Add(hash);
        }
        
        return true;
    }
    
    /// <summary>
    /// Builds a late join action packet.
    /// Format: [PacketType (1)][PlayerId (16)][Action (1)][SongTime (8)][PlayerNameLen (2)][PlayerName][MessageLen (2)][Message]
    /// </summary>
    public static byte[] BuildLateJoinActionPacket(Guid playerId, string playerName, LateJoinAction action, string message, double songTime = 0)
    {
        playerName ??= string.Empty;
        message ??= string.Empty;
        
        byte[] nameBytes = Encoding.UTF8.GetBytes(playerName);
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        
        int size = 1 + 16 + 1 + 8 + 2 + nameBytes.Length + 2 + messageBytes.Length; // Added 8 bytes for songTime
        byte[] buffer = new byte[size];
        int offset = 0;
        
        buffer[offset++] = (byte)PacketType.LateJoinAction;
        
        var playerIdBytes = playerId.ToByteArray();
        Array.Copy(playerIdBytes, 0, buffer, offset, 16);
        offset += 16;
        
        buffer[offset++] = (byte)action;
        
        // Write song time as 8 bytes (double)
        byte[] songTimeBytes = BitConverter.GetBytes(songTime);
        Array.Copy(songTimeBytes, 0, buffer, offset, 8);
        offset += 8;
        
        WriteShortString(buffer, ref offset, nameBytes);
        WriteShortString(buffer, ref offset, messageBytes);
        
        return buffer;
    }
    
    /// <summary>
    /// Parses a late join action packet.
    /// </summary>
    public static bool TryParseLateJoinActionPacket(ReadOnlySpan<byte> data, out Guid playerId, out string playerName, out LateJoinAction action, out string message, out double songTime)
    {
        playerId = Guid.Empty;
        playerName = string.Empty;
        action = LateJoinAction.NormalJoin;
        message = string.Empty;
        songTime = 0;
        
        // Minimum size: type (1) + playerId (16) + action (1) + songTime (8) + nameLen (2) + msgLen (2) = 30
        if (data.Length < 30)
            return false;
        
        int offset = 1;
        
        playerId = new Guid(data.Slice(offset, 16).ToArray());
        offset += 16;
        
        action = (LateJoinAction)data[offset++];
        
        // Read song time (8 bytes double)
        songTime = BitConverter.ToDouble(data.Slice(offset, 8).ToArray(), 0);
        offset += 8;
        
        if (!TryReadShortString(data, ref offset, out playerName))
            return false;
        
        if (!TryReadShortString(data, ref offset, out message))
            return false;
        
        return true;
    }
    
    /// <summary>
    /// Builds a setlist abort packet.
    /// Format: [PacketType (1)][ReasonLen (2)][Reason]
    /// </summary>
    public static byte[] BuildSetlistAbortPacket(string reason)
    {
        reason ??= string.Empty;
        byte[] reasonBytes = Encoding.UTF8.GetBytes(reason);
        
        int size = 1 + 2 + reasonBytes.Length;
        byte[] buffer = new byte[size];
        int offset = 0;
        
        buffer[offset++] = (byte)PacketType.SetlistAbort;
        WriteShortString(buffer, ref offset, reasonBytes);
        
        return buffer;
    }
    
    /// <summary>
    /// Parses a setlist abort packet.
    /// </summary>
    public static bool TryParseSetlistAbortPacket(ReadOnlySpan<byte> data, out string reason)
    {
        reason = string.Empty;
        
        if (data.Length < 3)
            return false;
        
        int offset = 1;
        return TryReadShortString(data, ref offset, out reason);
    }
    
    #region Helper Methods
    
    private static void WriteShortString(byte[] buffer, ref int offset, byte[] stringBytes)
    {
        buffer[offset++] = (byte)(stringBytes.Length >> 8);
        buffer[offset++] = (byte)(stringBytes.Length & 0xFF);
        Array.Copy(stringBytes, 0, buffer, offset, stringBytes.Length);
        offset += stringBytes.Length;
    }
    
    private static bool TryReadShortString(ReadOnlySpan<byte> data, ref int offset, out string result)
    {
        result = string.Empty;
        
        if (offset + 2 > data.Length)
            return false;
        
        int length = (data[offset++] << 8) | data[offset++];
        
        if (offset + length > data.Length)
            return false;
        
        result = Encoding.UTF8.GetString(data.Slice(offset, length).ToArray());
        offset += length;
        return true;
    }
    
    #endregion
}

#endregion
