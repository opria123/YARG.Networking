using System;
using System.Collections.Generic;

namespace YARG.Net.Packets;

/// <summary>
/// Sent by clients to upload chunks of their song library hashes to the server.
/// The server uses this to compute the intersection of all player libraries.
/// </summary>
public sealed record SongLibraryChunkPacket(
    Guid SessionId,
    byte[] HashData,
    bool IsFirstChunk,
    bool IsFinalChunk) : IPacketPayload;

/// <summary>
/// Sent by the server to broadcast the shared (intersected) song hashes to all clients.
/// </summary>
public sealed record SharedSongsChunkPacket(
    Guid LobbyId,
    byte[] HashData,
    bool IsFirstChunk,
    bool IsFinalChunk) : IPacketPayload;

/// <summary>
/// Sent by the server to clear the shared songs list (e.g., when a player disconnects).
/// </summary>
public sealed record ClearSharedSongsPacket(
    Guid LobbyId) : IPacketPayload;

#region Binary Packet Builders

/// <summary>
/// Binary packet builder/parser for song library chunk messages.
/// </summary>
public static class SongLibraryBinaryPackets
{
    /// <summary>
    /// Builds a SongLibraryChunk packet.
    /// Format: [PacketType (1)][isFirstChunk (1)][isFinalChunk (1)][dataLen (2)][hashData]
    /// </summary>
    public static byte[] BuildSongLibraryChunkPacket(byte[] hashData, bool isFirstChunk, bool isFinalChunk)
    {
        int dataLen = hashData?.Length ?? 0;
        byte[] message = new byte[5 + dataLen];
        message[0] = (byte)PacketType.SongLibraryChunk;
        message[1] = (byte)(isFirstChunk ? 1 : 0);
        message[2] = (byte)(isFinalChunk ? 1 : 0);
        message[3] = (byte)(dataLen >> 8);
        message[4] = (byte)(dataLen & 0xFF);

        if (dataLen > 0 && hashData != null)
            Array.Copy(hashData, 0, message, 5, dataLen);

        return message;
    }

    /// <summary>
    /// Builds a SharedSongsChunk packet.
    /// Format: [PacketType (1)][isFirstChunk (1)][isFinalChunk (1)][dataLen (2)][hashData]
    /// </summary>
    public static byte[] BuildSharedSongsChunkPacket(byte[] hashData, bool isFirstChunk, bool isFinalChunk)
    {
        int dataLen = hashData?.Length ?? 0;
        byte[] message = new byte[5 + dataLen];
        message[0] = (byte)PacketType.SharedSongsChunk;
        message[1] = (byte)(isFirstChunk ? 1 : 0);
        message[2] = (byte)(isFinalChunk ? 1 : 0);
        message[3] = (byte)(dataLen >> 8);
        message[4] = (byte)(dataLen & 0xFF);

        if (dataLen > 0 && hashData != null)
            Array.Copy(hashData, 0, message, 5, dataLen);

        return message;
    }

    /// <summary>
    /// Builds a ClearSharedSongs packet.
    /// </summary>
    public static byte[] BuildClearSharedSongsPacket()
    {
        return new byte[] { (byte)PacketType.ClearSharedSongs };
    }

    /// <summary>
    /// Parsed song library chunk data.
    /// </summary>
    public readonly struct ParsedChunk
    {
        public bool IsValid { get; init; }
        public bool IsFirstChunk { get; init; }
        public bool IsFinalChunk { get; init; }
        public int DataLength { get; init; }
        public int DataOffset { get; init; }
    }

    /// <summary>
    /// Parses a song library or shared songs chunk header.
    /// Data starts at DataOffset in the original span.
    /// </summary>
    public static ParsedChunk ParseChunkHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
            return new ParsedChunk { IsValid = false };

        int dataLen = (data[3] << 8) | data[4];

        if (data.Length < 5 + dataLen)
            return new ParsedChunk { IsValid = false };

        return new ParsedChunk
        {
            IsValid = true,
            IsFirstChunk = data[1] != 0,
            IsFinalChunk = data[2] != 0,
            DataLength = dataLen,
            DataOffset = 5
        };
    }
}

#endregion
