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
